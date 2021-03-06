﻿// Copyright 2014 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VsChromium.Commands;
using VsChromium.Features.ToolWindows.BuildExplorer;
using VsChromium.Features.ToolWindows.SourceExplorer;
using VsChromium.Package;

namespace VsChromium.Features.ToolWindows {
  [Export(typeof(ToolWindowAccessor))]
  public class ToolWindowAccessor {
    private readonly IVisualStudioPackageProvider _visualStudioPackageProvider;

    [ImportingConstructor]
    public ToolWindowAccessor(IVisualStudioPackageProvider visualStudioPackageProvider) {
      _visualStudioPackageProvider = visualStudioPackageProvider;
    }

    private ToolWindow GetToolWindow<ToolWindow>(Guid guid) where ToolWindow : class {
        var uiShell = _visualStudioPackageProvider.Package.VsUIShell;
        IVsWindowFrame windowFrame;
        uiShell.FindToolWindow(
            (uint)(__VSFINDTOOLWIN.FTW_fFindFirst | __VSFINDTOOLWIN.FTW_fForceCreate),
            guid, out windowFrame);
        if (windowFrame == null)
          return null;
        windowFrame.Show();

        object docView;
        windowFrame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out docView);
        return docView as ToolWindow;
    }

    public SourceExplorerToolWindow SourceExplorer {
      get {
        return GetToolWindow<SourceExplorerToolWindow>(GuidList.GuidSourceExplorerToolWindow);
      }
    }

    public BuildExplorerToolWindow BuildExplorer {
      get {
        return GetToolWindow<BuildExplorerToolWindow>(GuidList.GuidBuildExplorerToolWindow);
      }
    }
  }
}
