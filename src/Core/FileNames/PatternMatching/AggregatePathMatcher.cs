// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VsChromium.Core.FileNames.PatternMatching {
  public class AggregatePathMatcher : IPathMatcher {
    private HashSet<string> _fileExtensions;
    private IPathMatcher[] _pathMatchers;

    public AggregatePathMatcher(IEnumerable<PathMatcher> pathMatchers) {
      Optimize(pathMatchers.ToArray());
    }

    public bool MatchDirectoryName(string path, IPathComparer comparer) {
      if (path == null)
        throw new ArgumentNullException("path");

      if (_fileExtensions.Contains(Path.GetExtension(path)))
        return true;

      // Note: Use "for" loop to avoid allocation if using "Any()"
      for (var index = 0; index < _pathMatchers.Length; index++) {
        if (_pathMatchers[index].MatchDirectoryName(path, comparer))
          return true;
      }
      return false;
    }

    public bool MatchFileName(string path, IPathComparer comparer) {
      if (path == null)
        throw new ArgumentNullException("path");

      if (_fileExtensions.Contains(Path.GetExtension(path)))
        return true;

      // Note: Use "for" loop to avoid allocation if using "Any()"
      for (var index = 0; index < _pathMatchers.Length; index++) {
        if (_pathMatchers[index].MatchFileName(path, comparer))
          return true;
      }
      return false;
    }

    private void Optimize(IEnumerable<PathMatcher> matchers) {
      var pathMatchers = new List<IPathMatcher>();
      var fileExtensions = new HashSet<string>(SystemPathComparer.Instance.Comparer);

      foreach (var x in matchers) {
        string fileExtension;
        if (IsFileExtension(x, out fileExtension)) {
          fileExtensions.Add(fileExtension);
        } else {
          pathMatchers.Add(x);
        }
      }

      _pathMatchers = pathMatchers.ToArray();
      _fileExtensions = fileExtensions;
    }

    private bool IsFileExtension(PathMatcher pathMatcher, out string ext) {
      ext = "";
      var result =
        pathMatcher.Operators.Count == 3 &&
        pathMatcher.Operators[0] is OpRelativeDirectory &&
        pathMatcher.Operators[1] is OpAsterisk &&
        pathMatcher.Operators[2] is OpText &&
        IsFileExtensionString(((OpText)pathMatcher.Operators[2]).Text);
      if (result)
        ext = ((OpText)pathMatcher.Operators[2]).Text;
      return result;
    }

    private bool IsFileExtensionString(string text) {
      if (text.Length < 2)
        return false;

      if (text[0] != '.')
        return false;

      for (var i = 1; i < text.Length; i++) {
        if (text[i] == '.')
          return false;
        if (text[i] == Path.DirectorySeparatorChar)
          return false;
        if (text[i] == Path.AltDirectorySeparatorChar)
          return false;
        if (text[i] == Path.VolumeSeparatorChar)
          return false;
        if (Path.GetInvalidFileNameChars().Contains(text[i]))
          return false;
      }
      return true;
    }
  }
}
