﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Core.Linq;
using VsChromium.ServerProxy;

namespace VsChromium.Tests.Server {
  [TestClass]
  public class TestSearchFileContents : TestServerBase {
    [TestMethod]
    public void SingleOccurrenceWorks() {
      const string searchPattern = "Test directory looking like";

      using (var container = SetupMefContainer()) {
        using (var server = container.GetExport<ITypedRequestProcessProxy>().Value) {
          var testFile = GetChromiumEnlistmentFile();
          GetFileSystemTreeFromServer(server, testFile);

          VerifySearchFileContentsResponse(server, searchPattern, Options.MatchCase, testFile.Directory, 1);

          var searchPatternLower = searchPattern.ToLowerInvariant();
          VerifySearchFileContentsResponse(server, searchPatternLower, Options.None, testFile.Directory, 1);
        }
      }
    }

    [TestMethod]
    public void MultipleOccurrenceWorks() {
      const string searchPattern = "Nothing here. Just making sure the directory exists.";

      using (var container = SetupMefContainer()) {
        using (var server = container.GetExport<ITypedRequestProcessProxy>().Value) {
          var testFile = GetChromiumEnlistmentFile();
          GetFileSystemTreeFromServer(server, testFile);

          VerifySearchFileContentsResponse(server, searchPattern, Options.MatchCase, testFile.Directory, 3);

          var searchPatternLower = searchPattern.ToLowerInvariant();
          VerifySearchFileContentsResponse(server, searchPatternLower, Options.None, testFile.Directory, 3);
        }
      }
    }

    [TestMethod]
    public void SingleWildcardWorks() {
      const string searchPattern = "Test*looking";

      using (var container = SetupMefContainer()) {
        using (var server = container.GetExport<ITypedRequestProcessProxy>().Value) {
          var testFile = GetChromiumEnlistmentFile();
          GetFileSystemTreeFromServer(server, testFile);

          VerifySearchFileContentsResponse(server, searchPattern, Options.MatchCase, testFile.Directory, 1, 0, 22);

          var searchPatternLower = searchPattern.ToLowerInvariant();
          VerifySearchFileContentsResponse(server, searchPatternLower, Options.None, testFile.Directory, 1, 0, 22);
        }
      }
    }

    [TestMethod]
    public void SingleWildcardWorks2() {
      const string searchPattern = "looking*like";

      using (var container = SetupMefContainer()) {
        using (var server = container.GetExport<ITypedRequestProcessProxy>().Value) {
          var testFile = GetChromiumEnlistmentFile();
          GetFileSystemTreeFromServer(server, testFile);

          VerifySearchFileContentsResponse(server, searchPattern, Options.MatchCase, testFile.Directory, 1, 15, 12);

          var searchPatternLower = searchPattern.ToLowerInvariant();
          VerifySearchFileContentsResponse(server, searchPatternLower, Options.None, testFile.Directory, 1, 15, 12);
        }
      }
    }

    [TestMethod]
    public void MultipleWildcardsWorks() {
      const string searchPattern = "Test*looking*like";

      using (var container = SetupMefContainer()) {
        using (var server = container.GetExport<ITypedRequestProcessProxy>().Value) {
          var testFile = GetChromiumEnlistmentFile();
          GetFileSystemTreeFromServer(server, testFile);

          VerifySearchFileContentsResponse(server, searchPattern, Options.MatchCase, testFile.Directory, 1, 0, 27);

          var searchPatternLower = searchPattern.ToLowerInvariant();
          VerifySearchFileContentsResponse(server, searchPatternLower, Options.None, testFile.Directory, 1, 0, 27);
        }
      }
    }

    [TestMethod]
    public void MultipleWildcardsWorks2() {
      const string searchPattern = "Test*directory*looking*like";

      using (var container = SetupMefContainer()) {
        using (var server = container.GetExport<ITypedRequestProcessProxy>().Value) {
          var testFile = GetChromiumEnlistmentFile();
          GetFileSystemTreeFromServer(server, testFile);

          VerifySearchFileContentsResponse(server, searchPattern, Options.MatchCase, testFile.Directory, 1, 0, 27);

          var searchPatternLower = searchPattern.ToLowerInvariant();
          VerifySearchFileContentsResponse(server, searchPatternLower, Options.None, testFile.Directory, 1, 0, 27);
        }
      }
    }

    [TestMethod]
    public void MultipleWildcardsWorks3() {
      const string searchPattern = "directory*looking*like";

      using (var container = SetupMefContainer()) {
        using (var server = container.GetExport<ITypedRequestProcessProxy>().Value) {
          var testFile = GetChromiumEnlistmentFile();
          GetFileSystemTreeFromServer(server, testFile);

          VerifySearchFileContentsResponse(server, searchPattern, Options.MatchCase, testFile.Directory, 1, 5, 22);

          var searchPatternLower = searchPattern.ToLowerInvariant();
          VerifySearchFileContentsResponse(server, searchPatternLower, Options.None, testFile.Directory, 1, 5, 22);
        }
      }
    }

    [TestMethod]
    public void EscapeWildcardWorks() {
      const string searchPattern = @"foo\* bar";

      using (var container = SetupMefContainer()) {
        using (var server = container.GetExport<ITypedRequestProcessProxy>().Value) {
          var testFile = GetChromiumEnlistmentFile();
          GetFileSystemTreeFromServer(server, testFile);

          VerifySearchFileContentsResponse(server, searchPattern, Options.MatchCase, testFile.Directory, 1, 7 ,8);

          var searchPatternLower = searchPattern.ToLowerInvariant();
          VerifySearchFileContentsResponse(server, searchPatternLower, Options.None, testFile.Directory, 1, 7, 8);
        }
      }
    }

    [TestMethod]
    public void EscapeWildcardWorks2() {
      const string searchPattern = @"foo\*\\bar";

      using (var container = SetupMefContainer()) {
        using (var server = container.GetExport<ITypedRequestProcessProxy>().Value) {
          var testFile = GetChromiumEnlistmentFile();
          GetFileSystemTreeFromServer(server, testFile);

          VerifySearchFileContentsResponse(server, searchPattern, Options.MatchCase, testFile.Directory, 1, 39, 8);

          var searchPatternLower = searchPattern.ToLowerInvariant();
          VerifySearchFileContentsResponse(server, searchPatternLower, Options.None, testFile.Directory, 1, 39, 8);
        }
      }
    }

    [Flags]
    private enum Options {
      None = 0,
      MatchCase = 1,
    }

    private static void VerifySearchFileContentsResponse(
        ITypedRequestProcessProxy server,
        string searchPattern,
        Options options,
        DirectoryInfo chromiumDirectory,
        int occurrenceCount,
        params int[] positionsAndLengths) {
      var response = SendRequest<SearchFileContentsResponse>(server, new SearchFileContentsRequest {
        SearchParams = new SearchParams {
          SearchString = searchPattern,
          MaxResults = 2000,
          MatchCase = ((options & Options.MatchCase) != 0)
        }
      }, ServerResponseTimeout)();
      Assert.IsNotNull(response, "Server did not respond within timeout.");
      Assert.IsNotNull(response.SearchResults);
      Assert.IsNotNull(response.SearchResults.Entries);

      Assert.AreEqual(1, response.SearchResults.Entries.Count);
      var chromiumEntry = response.SearchResults.Entries[0] as DirectoryEntry;
      Assert.IsNotNull(chromiumEntry);
      Assert.AreEqual(chromiumDirectory.FullName, chromiumEntry.Name);

      chromiumEntry.Entries.ForAll((index, x) => {
        Debug.WriteLine(string.Format("File name: \"{0}\"", x.Name));
        Assert.IsNotNull(x.Data);
        Assert.IsTrue(x.Data is FilePositionsData);
        ((FilePositionsData)x.Data).Positions.ForEach(y => {
          Debug.WriteLine(string.Format("   Text position: offset={0}, length={1}", y.Position, y.Length));
          if (positionsAndLengths != null && positionsAndLengths.Length > 0) {
            Assert.AreEqual(positionsAndLengths[index * 2], y.Position);
            Assert.AreEqual(positionsAndLengths[(index * 2) + 1], y.Length);
          }
        });
      });
      Assert.AreEqual(occurrenceCount, chromiumEntry.Entries.Count);
    }
  }
}
