﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using VsChromium.Core;
using VsChromium.Core.FileNames;
using VsChromium.Core.FileNames.PatternMatching;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Core.Linq;
using VsChromium.Server.FileSystem;
using VsChromium.Server.FileSystemContents;
using VsChromium.Server.FileSystemDatabase;
using VsChromium.Server.FileSystemNames;
using VsChromium.Server.FileSystemSnapshot;
using VsChromium.Server.NativeInterop;
using VsChromium.Server.Operations;
using VsChromium.Server.Projects;
using VsChromium.Server.Threads;

namespace VsChromium.Server.Search {
  [Export(typeof(ISearchEngine))]
  public class SearchEngine : ISearchEngine {
    private const int MinimumSearchPatternLength = 2;
    private readonly ICustomThreadPool _customThreadPool;
    private readonly IFileDatabaseFactory _fileDatabaseFactory;
    private readonly IFileSystemNameFactory _fileSystemNameFactory;
    private readonly object _lock = new Object();
    private readonly IProjectDiscovery _projectDiscovery;
    private readonly ISearchStringParser _searchStringParser;
    private readonly IOperationProcessor<OperationResultEventArgs> _fileLoadingOperationProcessor;
    private readonly TaskCancellation _taskCancellation = new TaskCancellation();
    private volatile IFileDatabase _currentFileDatabase;

    [ImportingConstructor]
    public SearchEngine(
      IFileSystemProcessor fileSystemProcessor,
      IFileSystemNameFactory fileSystemNameFactory,
      ICustomThreadPool customThreadPool,
      IFileDatabaseFactory fileDatabaseFactory,
      IProjectDiscovery projectDiscovery,
      ISearchStringParser searchStringParser,
      IOperationProcessor<OperationResultEventArgs> fileLoadingOperationProcessor) {
      _fileSystemNameFactory = fileSystemNameFactory;
      _customThreadPool = customThreadPool;
      _fileDatabaseFactory = fileDatabaseFactory;
      _projectDiscovery = projectDiscovery;
      _searchStringParser = searchStringParser;
      _fileLoadingOperationProcessor = fileLoadingOperationProcessor;

      // Create a "Null" state
      _currentFileDatabase = _fileDatabaseFactory.CreateEmpty();

      // Setup computing a new state everytime a new tree is computed.
      fileSystemProcessor.SnapshotComputed += FileSystemProcessorOnSnapshotComputed;
      fileSystemProcessor.FilesChanged += FileSystemProcessorOnFilesChanged;
    }

    public SearchFileNamesResult SearchFileNames(SearchParams searchParams) {
      var matchFunction = SearchPreProcessParams<FileName>(searchParams, MatchFileName, MatchFileRelativePath);
      if (matchFunction == null)
        return SearchFileNamesResult.Empty;

      // taskCancellation is used to make sure we cancel previous tasks as fast
      // as possible to avoid using too many CPU resources if the caller keeps
      // asking us to search for things. Note that this assumes the caller is
      // only interested in the result of the *last* query, while the previous
      // queries will throw an OperationCanceled exception.
      _taskCancellation.CancelAll();

      var matches = _currentFileDatabase.FileNames
        .AsParallel()
        // We need the line below because of "Take" (.net 4.0 PLinq limitation)
        .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
        .WithCancellation(_taskCancellation.GetNewToken())
        .Where(item => matchFunction(item))
        .Take(searchParams.MaxResults)
        .ToList();

      return new SearchFileNamesResult {
        FileNames = matches,
        TotalCount = _currentFileDatabase.FileNames.Count
      };
    }

    public SearchDirectoryNamesResult SearchDirectoryNames(SearchParams searchParams) {
      var matchFunction = SearchPreProcessParams<DirectoryName>(searchParams, MatchDirectoryName,
                                                                MatchDirectoryRelativePath);
      if (matchFunction == null)
        return SearchDirectoryNamesResult.Empty;

      // taskCancellation is used to make sure we cancel previous tasks as fast
      // as possible to avoid using too many CPU resources if the caller keeps
      // asking us to search for things. Note that this assumes the caller is
      // only interested in the result of the *last* query, while the previous
      // queries will throw an OperationCanceled exception.
      _taskCancellation.CancelAll();

      var matches = _currentFileDatabase.DirectoryNames
        .AsParallel()
        // We need the line below because of "Take" (.net 4.0 PLinq limitation)
        .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
        .WithCancellation(_taskCancellation.GetNewToken())
        .Where(item => matchFunction(item))
        .Take(searchParams.MaxResults)
        .ToList();

      return new SearchDirectoryNamesResult {
        DirectoryNames = matches,
        TotalCount = _currentFileDatabase.DirectoryNames.Count
      };
    }

    public SearchFileContentsResult SearchFileContents(SearchParams searchParams) {
      var parsedSearchString = _searchStringParser.Parse(searchParams.SearchString ?? "");
      // Don't search empty or very small strings -- no significant results.
      if (string.IsNullOrWhiteSpace(parsedSearchString.MainEntry.Text) ||
          (parsedSearchString.MainEntry.Text.Length < MinimumSearchPatternLength)) {
        return SearchFileContentsResult.Empty;
      }

      using (var searchContentsData = new SearchContentsData(parsedSearchString, CreateSearchAlgorithms(parsedSearchString, searchParams.MatchCase))) {
        // taskCancellation is used to make sure we cancel previous tasks as
        // fast as possible to avoid using too many CPU resources if the caller
        // keeps asking us to search for things. Note that this assumes the
        // caller is only interested in the result of the *last* query, while
        // the previous queries will throw an OperationCanceled exception.
        _taskCancellation.CancelAll();
        var cancellationToken = _taskCancellation.GetNewToken();
        return DoSearchFileContents(searchContentsData, searchParams.MaxResults, cancellationToken);
      }
    }

    private static List<SearchContentsAlgorithms> CreateSearchAlgorithms(ParsedSearchString parsedSearchString, bool matchCase) {
      var searchOptions = matchCase ? NativeMethods.SearchOptions.kMatchCase : NativeMethods.SearchOptions.kNone;
      return parsedSearchString.EntriesBeforeMainEntry
        .Concat(new[] { parsedSearchString.MainEntry })
        .Concat(parsedSearchString.EntriesAfterMainEntry)
        .OrderBy(x => x.Index)
        .Select(entry => {
          var a1 = AsciiFileContents.CreateSearchAlgo(entry.Text, searchOptions);
          var a2 = UTF16FileContents.CreateSearchAlgo(entry.Text, searchOptions);
          return new SearchContentsAlgorithms(a1, a2);
        })
        .ToList();
    }

    private SearchFileContentsResult DoSearchFileContents(SearchContentsData searchContentsData, int maxResults, CancellationToken cancellationToken) {
      var taskResults = new TaskResultCounter(maxResults);
      var searchedFileCount = 0;
      var matches = _currentFileDatabase.FilesWithContents
        .AsParallel()
        .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
        .WithCancellation(cancellationToken)
        .Where(x => !taskResults.Done)
        .Select(item => {
          Interlocked.Increment(ref searchedFileCount);
          return MatchFileContents(item, searchContentsData, taskResults);
        })
        .Where(r => r != null)
        .ToList();

      return new SearchFileContentsResult {
        Entries = matches,
        SearchedFileCount = searchedFileCount,
        TotalFileCount = _currentFileDatabase.FilesWithContents.Count,
        HitCount = taskResults.Count,
      };
    }

    public IEnumerable<FileExtract> GetFileExtracts(FullPathName path, IEnumerable<FilePositionSpan> spans) {
      var filename = FileSystemNameFactoryExtensions.GetProjectFileName(_fileSystemNameFactory, _projectDiscovery, path);
      if (filename == null)
        return Enumerable.Empty<FileExtract>();

      return _currentFileDatabase.GetFileExtracts(filename.Item2, spans);
    }

    public event EventHandler<OperationEventArgs> FilesLoading;

    public event EventHandler<OperationResultEventArgs> FilesLoaded;

    protected virtual void OnFilesLoaded(OperationResultEventArgs e) {
      EventHandler<OperationResultEventArgs> handler = FilesLoaded;
      if (handler != null) handler(this, e);
    }

    protected virtual void OnFilesLoading(OperationEventArgs e) {
      EventHandler<OperationEventArgs> handler = FilesLoading;
      if (handler != null) handler(this, e);
    }

    private void FileSystemProcessorOnFilesChanged(object sender, FilesChangedEventArgs filesChangedEventArgs) {
      _customThreadPool.RunAsync(() => UpdateFileContents(filesChangedEventArgs.ChangedFiles));
    }

    private void UpdateFileContents(IEnumerable<Tuple<IProject, FileName>> paths) {
      _fileLoadingOperationProcessor.Execute(new OperationInfo<OperationResultEventArgs> {
        OnBeforeExecute = args => OnFilesLoading(args),
        OnAfterExecute = args => OnFilesLoaded(args),
        Execute = _ => {
          paths.ForAll(x => _currentFileDatabase.UpdateFileContents(x));
          return new OperationResultEventArgs();
        }
      });
    }

    private void FileSystemProcessorOnSnapshotComputed(object sender, SnapshotComputedEventArgs e) {
      if (e.NewSnapshot != null) {
        _customThreadPool.RunAsync(() => ComputeNewState(e.NewSnapshot));
      }
    }

    private Func<T, bool> SearchPreProcessParams<T>(
      SearchParams searchParams,
      Func<IPathMatcher, T, IPathComparer, bool> matchName,
      Func<IPathMatcher, T, IPathComparer, bool> matchRelativeName) where T : FileSystemName {
      var pattern = ConvertUserSearchStringToSearchPattern(searchParams);
      if (pattern == null)
        return null;

      var matcher = FileNameMatching.ParsePattern(pattern);

      var comparer = searchParams.MatchCase ?
                       CaseSensitivePathComparer.Instance :
                       CaseInsensitivePathComparer.Instance;
      if (pattern.Contains(Path.DirectorySeparatorChar))
        return (item) => matchRelativeName(matcher, item, comparer);
      else
        return (item) => matchName(matcher, item, comparer);
    }

    private static string ConvertUserSearchStringToSearchPattern(SearchParams searchParams) {
      var pattern = searchParams.SearchString ?? "";

      pattern = pattern.Trim();
      if (string.IsNullOrWhiteSpace(pattern))
        return null;

      // We use "\\" internally for paths and patterns.
      pattern = pattern.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

      // Exception to ".gitignore" syntax: If the search string doesn't contain any special
      // character, surround the pattern with "*" so that we match sub-strings.
      // TODO(rpaquay): What about "."? Special or not?
      if (pattern.IndexOf(Path.DirectorySeparatorChar) < 0 &&
          pattern.IndexOf('*') < 0) {
        pattern = "*" + pattern + "*";
      }

      return pattern;
    }

    private void ComputeNewState(FileSystemTreeSnapshot newSnapshot) {
      _fileLoadingOperationProcessor.Execute(new OperationInfo<OperationResultEventArgs> {
        OnBeforeExecute = args => OnFilesLoading(args),
        OnAfterExecute = args => OnFilesLoaded(args),
        Execute = _ => {
          Logger.Log("Computing new state of file database from file system tree.");
          var sw = Stopwatch.StartNew();

          var oldState = _currentFileDatabase;
          var newState = _fileDatabaseFactory.CreateIncremental(oldState, newSnapshot);

          sw.Stop();
          Logger.Log(">>>>>>>> Done computing new state of file database from file system tree in {0:n0} msec.",
            sw.ElapsedMilliseconds);
          Logger.LogMemoryStats();

          // Swap states
          lock (_lock) {
            _currentFileDatabase = newState;
          }

          return new OperationResultEventArgs();
        }
      });
    }

    private bool MatchFileName(IPathMatcher matcher, FileName fileName, IPathComparer comparer) {
      return matcher.MatchFileName(fileName.RelativePathName.Name, comparer);
    }

    private bool MatchFileRelativePath(IPathMatcher matcher, FileName fileName, IPathComparer comparer) {
      return matcher.MatchFileName(fileName.RelativePathName.RelativeName, comparer);
    }

    private bool MatchDirectoryName(IPathMatcher matcher, DirectoryName directoryName, IPathComparer comparer) {
      // "Chromium" root directories make it through here, skip them.
      if (directoryName.IsAbsoluteName)
        return false;

      return matcher.MatchDirectoryName(directoryName.RelativePathName.Name, comparer);
    }

    private bool MatchDirectoryRelativePath(IPathMatcher matcher, DirectoryName directoryName, IPathComparer comparer) {
      // "Chromium" root directories make it through here, skip them.
      if (directoryName.IsAbsoluteName)
        return false;

      return matcher.MatchDirectoryName(directoryName.RelativePathName.RelativeName, comparer);
    }

    private static FileSearchResult MatchFileContents(FileData fileData, SearchContentsData searchContentsData, TaskResultCounter taskResultCounter) {
      var spans = fileData.Contents.Search(searchContentsData);
      if (spans.Count == 0)
        return null;

      taskResultCounter.Add(spans.Count);

      return new FileSearchResult {
        FileName = fileData.FileName,
        Spans = spans
      };
    }
  }
}
