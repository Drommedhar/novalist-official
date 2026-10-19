{pkgs, lib, ...}: {
  languages.dotnet = {
    enable = true;
    package = pkgs.dotnetCorePackages.sdk_10_0;
  };

  languages.javascript = {
    enable = true;
    package = pkgs.nodejs_22;
    npm.enable = true;
  };

  packages = [pkgs.git pkgs.python3];

  env = {
    DOTNET_CLI_TELEMETRY_OPTOUT = "1";
    DOTNET_NOLOGO = "1";
  } // lib.optionalAttrs pkgs.stdenv.hostPlatform.isLinux {
    # Use Nix's patched Electron instead of npm's generic Linux binary.
    ELECTRON_SKIP_BINARY_DOWNLOAD = "1";
    ELECTRON_OVERRIDE_DIST_PATH = "${pkgs.electron_42.dist}";
    # electron-vite resolves the executable itself rather than using require('electron').
    ELECTRON_EXEC_PATH = "${pkgs.electron_42}/bin/electron";
  };

  # Stable SDK path for IDEs such as Rider, including the sdk/ directory.
  enterShell = ''
    ln -sfn "$DOTNET_ROOT" "$DEVENV_STATE/dotnet"
  '';
}
