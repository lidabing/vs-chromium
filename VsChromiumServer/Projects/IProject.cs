﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

namespace VsChromiumServer.Projects {
  public interface IProject {
    string RootPath { get; }

    IDirectoryFilter DirectoryFilter { get; }
    IFileFilter FileFilter { get; }
    ISearchableFilesFilter SearchableFilesFilter { get; }
  }
}
