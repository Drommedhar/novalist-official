{
  pkgs,
  lib,
  config,
  ...
}: let
  novalist = pkgs.callPackage ./nix/package.nix {
    version = (builtins.fromJSON (builtins.readFile ./app/package.json)).version;
    src = lib.cleanSourceWith {
      src = ./.;
      filter = path: type:
        lib.cleanSourceFilter path type
        && !(type
          == "directory"
          && builtins.elem (baseNameOf path) [
            ".devenv"
            ".direnv"
            ".codegraph"
            ".worktrees"
            ".idea"
            ".vs"
            "node_modules"
            "out"
            "bin"
            "obj"
            "dist"
            "dist-backend"
            "artifacts"
            "TestResults"
          ]);
    };
  };
in {
  languages.dotnet = {
    enable = true;
    package = pkgs.dotnetCorePackages.sdk_10_0;
  };

  languages.javascript = {
    enable = true;
    package = pkgs.nodejs_24;
    npm.enable = true;
  };

  packages = [pkgs.git pkgs.python3];

  env =
    {
      DOTNET_CLI_TELEMETRY_OPTOUT = "1";
      DOTNET_NOLOGO = "1";
    }
    // lib.optionalAttrs pkgs.stdenv.hostPlatform.isLinux {
      # Use Nix's patched Electron instead of npm's generic Linux binary.
      ELECTRON_SKIP_BINARY_DOWNLOAD = "1";
      ELECTRON_OVERRIDE_DIST_PATH = "${pkgs.electron_44.dist}";
      # electron-vite resolves the executable itself rather than using require('electron').
      ELECTRON_EXEC_PATH = "${pkgs.electron_44}/bin/electron";
    };

  outputs = lib.optionalAttrs pkgs.stdenv.hostPlatform.isLinux {
    inherit novalist;
  };

  scripts = lib.optionalAttrs pkgs.stdenv.hostPlatform.isLinux {
    build = {
      description = "Build the current checkout through the nixpkgs-style package";
      exec = ''
        set -euo pipefail
        cd "$DEVENV_ROOT"
        devenv --no-tui build outputs.novalist | ${lib.getExe pkgs.jq} -er '."outputs.novalist"'
      '';
    };
    run = {
      description = "Build and launch the packaged Novalist client";
      exec = ''
        set -euo pipefail
        package="$(${config.scripts.build.package}/bin/build)"
        exec "$package/bin/novalist" "$@"
      '';
    };
  };

  # Stable SDK path for IDEs such as Rider, including the sdk/ directory.
  enterShell = ''
    ln -sfn "$DOTNET_ROOT" "$DEVENV_STATE/dotnet"
  '';
}
