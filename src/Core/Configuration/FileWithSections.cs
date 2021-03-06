﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VsChromium.Core.Configuration {
  public class FileWithSections : IFileWithSections {
    private readonly string _filename;
    private readonly Lazy<Dictionary<string, List<string>>> _sections;
    private Func<IEnumerable<string>, IEnumerable<string>> _postProcessing;

    public FileWithSections(string filename) {
      _filename = filename;
      _sections = new Lazy<Dictionary<string, List<string>>>(ReadFile);
    }

    private Dictionary<string, List<string>> ReadFile() {
      var lines = File.ReadAllLines(_filename);
      var result = new Dictionary<string, List<string>>();
      var sectionName = "";
      foreach (var line in lines) {
        var newSectionName = ParseSectionName(line);
        if (newSectionName != null) {
          sectionName = newSectionName;
          continue;
        }

        // Add line to section
        List<string> section;
        if (!result.TryGetValue(sectionName, out section)) {
          section = new List<string>();
          result.Add(sectionName, section);
        }
        result[sectionName].Add(line);
      }

      return result.ToDictionary(x => x.Key, x => _postProcessing(x.Value).ToList());
    }

    private string ParseSectionName(string line) {
      line = line.Trim();
      if (line.Length > 2 && line.First() == '[' && line.Last() == ']') {
        return line.Substring(1, line.Length - 2).Trim();
      }
      return null;
    }

    public IEnumerable<string> ReadSection(string name, Func<IEnumerable<string>, IEnumerable<string>> postProcessing) {
      try {
        _postProcessing = postProcessing;
        List<string> section;
        if (!_sections.Value.TryGetValue(name, out section))
          return Enumerable.Empty<string>();

        return section;
      }
      finally {
        _postProcessing = null;
      }
    }
  }
}
