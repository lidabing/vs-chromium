﻿namespace VsChromium.Package {
  interface IPackagePreInitializer {
    int Priority { get; }
    void Run(IVisualStudioPackage package);
  }
}