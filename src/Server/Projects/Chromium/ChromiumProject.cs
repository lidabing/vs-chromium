// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using VsChromium.Core.Configuration;
using VsChromium.Core.FileNames;

namespace VsChromium.Server.Projects.Chromium {
  public class ChromiumProject : IProject {
    private readonly FullPathName _rootPath;
    private readonly IDirectoryFilter _directoryFilter;
    private readonly IFileFilter _fileFilter;
    private readonly ISearchableFilesFilter _searchableFilesFilter;

    public ChromiumProject(IConfigurationSectionProvider configurationSectionProvider, FullPathName rootPath) {
      _rootPath = rootPath;
      _directoryFilter = new DirectoryFilter(configurationSectionProvider);
      _fileFilter = new FileFilter(configurationSectionProvider);
      _searchableFilesFilter = new SearchableFilesFilter(configurationSectionProvider);
    }

    public FullPathName RootPath { get { return _rootPath; } }

    public IDirectoryFilter DirectoryFilter { get { return _directoryFilter; } }

    public IFileFilter FileFilter { get { return _fileFilter; } }

    public ISearchableFilesFilter SearchableFilesFilter { get { return _searchableFilesFilter; } }
  }
}
