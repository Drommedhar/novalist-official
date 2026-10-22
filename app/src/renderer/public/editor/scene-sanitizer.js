var NovalistSceneSanitizer = (() => {
  var __defProp = Object.defineProperty;
  var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
  var __getOwnPropNames = Object.getOwnPropertyNames;
  var __hasOwnProp = Object.prototype.hasOwnProperty;
  var __export = (target, all) => {
    for (var name in all)
      __defProp(target, name, { get: all[name], enumerable: true });
  };
  var __copyProps = (to, from, except, desc) => {
    if (from && typeof from === "object" || typeof from === "function") {
      for (let key of __getOwnPropNames(from))
        if (!__hasOwnProp.call(to, key) && key !== except)
          __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
    }
    return to;
  };
  var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

  // src/shared/sceneSanitizer.ts
  var sceneSanitizer_exports = {};
  __export(sceneSanitizer_exports, {
    createSceneSanitizer: () => createSceneSanitizer
  });
  function createSceneSanitizer(window, purify) {
    const document = window.document.implementation.createHTMLDocument("");
    const form = document.createElement("form");
    const windowNames = /* @__PURE__ */ new Set();
    for (let owner = window; owner; owner = Object.getPrototypeOf(owner)) {
      for (const name of Object.getOwnPropertyNames(owner)) windowNames.add(name);
    }
    purify.addHook("uponSanitizeAttribute", (node, attribute, config) => {
      if (node.namespaceURI === "http://www.w3.org/1999/xhtml" && attribute.attrName === "id" && attribute.attrValue in form && !(attribute.attrValue in document) && !windowNames.has(attribute.attrValue) && !config.FORBID_ATTR?.includes("id") && config.FORBID_TAGS?.includes("form")) {
        attribute.forceKeepAttr = true;
      }
    });
    return {
      sanitizeSceneHtml: (html) => purify.sanitize(html || "", options()),
      sanitizeSceneFragment: (html) => purify.sanitize(html || "", { ...options(), RETURN_DOM_FRAGMENT: true })
    };
  }
  function options() {
    return {
      USE_PROFILES: { html: true },
      ADD_ATTR: ["contenteditable"],
      FORBID_TAGS: ["style", "form", "input", "button", "textarea", "select", "option"],
      ALLOWED_URI_REGEXP: /^(?:(?:(?:f|ht)tps?|mailto|tel|callto|sms|cid|xmpp|matrix|novalist|novalist-project|novalist-asset|novalist-audio):|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i
    };
  }
  return __toCommonJS(sceneSanitizer_exports);
})();
