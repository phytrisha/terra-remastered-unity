// Rebuild the scene. Args: "quick" skips the overlap pass (iteration only; commit full builds).
RainforestBuilder.QuickBuild = ARGS.Contains("quick");
return RainforestBuilder.Build();
