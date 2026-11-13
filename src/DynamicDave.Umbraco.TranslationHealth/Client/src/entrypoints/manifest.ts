export const manifests: Array<UmbExtensionManifest> = [
  {
    name: "Dynamic Dave Umbraco Translation Health Entrypoint",
    alias: "DynamicDave.Umbraco.TranslationHealth.Entrypoint",
    type: "backofficeEntryPoint",
    js: () => import("./entrypoint.js"),
  },
];
