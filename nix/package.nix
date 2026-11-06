{
  lib,
  buildNpmPackage,
  buildDotnetModule,
  buildFHSEnv,
  copyDesktopItems,
  dotnetCorePackages,
  electron_44,
  makeDesktopItem,
  makeWrapper,
  nodejs_24,
  src,
  version,
  withRocm ? false,
}:
let
  electron = electron_44;
in
buildNpmPackage (finalAttrs: {
  pname = "novalist";
  inherit version src;
  __structuredAttrs = true;
  sourceRoot = "${finalAttrs.src.name}/app";
  nodejs = nodejs_24;
  npmDepsHash = "sha256-Pl1RI3sKWnx4vsu/93Rhf+SP/D/oZNfYxAjWrHBJ5OU=";
  npmFlags = [ "--ignore-scripts" ];
  makeCacheWritable = true;
  env.ELECTRON_SKIP_BINARY_DOWNLOAD = "1";
  nativeBuildInputs = [ copyDesktopItems makeWrapper ];

  preBuild = ''
    npm pkg set version=${finalAttrs.version}
  '';
  npmBuildScript = "build";

  installPhase = ''
    runHook preInstall
    mkdir -p $out/share/novalist
    cp -r out package.json resources $out/share/novalist/
    makeWrapper ${lib.getExe electron} $out/bin/novalist \
      --add-flags $out/share/novalist \
      --add-flags "\''${NIXOS_OZONE_WL:+\''${WAYLAND_DISPLAY:+--ozone-platform-hint=auto --enable-features=WaylandWindowDecorations --enable-wayland-ime=true}}" \
      --set NOVALIST_BACKEND_PATH ${lib.getExe finalAttrs.passthru.backendFhs} \
      --set NOVALIST_DISABLE_SELF_UPDATE 1 \
      --inherit-argv0
    install -Dm644 build/icon.png $out/share/icons/hicolor/512x512/apps/novalist.png
    runHook postInstall
  '';

  desktopItems = [ (makeDesktopItem {
    name = "novalist";
    exec = "novalist %U";
    icon = "novalist";
    desktopName = "Novalist";
    genericName = "Novel Writing";
    comment = finalAttrs.meta.description;
    categories = [ "Office" "WordProcessor" ];
    startupWMClass = "Novalist";
  }) ];

  passthru.backend = buildDotnetModule {
    pname = "${finalAttrs.pname}-backend";
    inherit (finalAttrs) version src;
    __structuredAttrs = true;
    projectFile = "Novalist.Backend/Novalist.Backend.csproj";
    nugetDeps = ./deps.json;
    postPatch = ''
      rm dotnet-tools.json
    '';
    dotnet-sdk = dotnetCorePackages.sdk_10_0;
    dotnet-runtime = dotnetCorePackages.aspnetcore_10_0;
    dotnetFlags = [
      "-p:VersionPrefix=${finalAttrs.version}"
      "-p:ContinuousIntegrationBuild=true"
      "-p:BuildAppleDesktopSpeech=false"
    ];
    executables = [ "Novalist.Backend" ];
    meta = finalAttrs.meta // {
      description = "Backend process of Novalist";
      mainProgram = "Novalist.Backend";
    };
  };

  passthru.backendFhs = buildFHSEnv {
    name = "novalist-backend";
    targetPkgs = pkgs: [ pkgs.numactl pkgs.stdenv.cc.cc.lib pkgs.zlib ]
      ++ lib.optionals withRocm (with pkgs.rocmPackages; [
        clr hipblas hipblaslt hipfft hiprand hipsolver hipsparse hipsparselt
        miopen rccl rocblas rocsolver roctracer
      ]);
    runScript = lib.getExe finalAttrs.passthru.backend;
    meta.mainProgram = "novalist-backend";
  };

  meta = {
    description = "Offline-first desktop application for writing novels";
    homepage = "https://github.com/Drommedhar/novalist-official";
    license = lib.licenses.mit;
    mainProgram = "novalist";
    platforms = lib.platforms.linux;
  };
})
