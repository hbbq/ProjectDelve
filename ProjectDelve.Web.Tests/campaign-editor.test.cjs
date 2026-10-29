const { test } = require("node:test");
const assert = require("node:assert/strict");

class Element {
  constructor(tag) { this.tagName = tag; this.children = []; this.dataset = {}; this.listeners = {}; this.attributes = {}; this.value = ""; this._text = ""; }
  set textContent(text) { this._text = String(text); this.children = []; }
  get textContent() { return this._text + this.children.map(child => child.textContent).join(""); }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this._text = ""; this.children = children; }
  setAttribute(key, value) { this.attributes[key] = value; }
  addEventListener(event, listener) { this.listeners[event] = listener; }
  focus() {}
  reportValidity() { return true; }
  all() { return [this, ...this.children.flatMap(child => child.all())]; }
  querySelectorAll(selector) { return this.all().slice(1).filter(child => selector.split(",").includes(child.tagName)); }
  querySelector() { return null; }
}
const scenario = (id, name) => ({ id, name, coverArt: null, revision: 1 });
const campaign = (overrides = {}) => ({ id: "test-campaign", name: "Evening adventures", description: null,
  coverArt: null, scenarioIds: ["one", "two"], revision: 4, ...overrides });
async function harness(current = campaign(), operation, prepareImage) {
  global.document = { createElement: tag => new Element(tag) };
  let confirms = 0, confirmResult = false;
  global.confirm = () => { confirms++; return confirmResult; };
  const root = new Element("section"), calls = [], saved = [];
  const library = { scenarios: [scenario("one", "First expedition"), scenario("two", "Second expedition"), scenario("three", "Third expedition")], campaigns: current ? [current] : [] };
  const { mountCampaignEditor } = await import("../ProjectDelve.Web/wwwroot/campaign-editor.js");
  const editor = mountCampaignEditor(root, {
    ...(prepareImage ? { prepareImage } : {}),
    request: async (path, options) => {
      calls.push({ path, ...options });
      if (!path) return structuredClone(library);
      if (operation) return operation(path, options);
      return options.method === "DELETE" ? null : { ...JSON.parse(options.body), id: "test-campaign", revision: 5 };
    },
    onSaved: result => saved.push(result)
  });
  await editor.open(current?.id ?? null);
  const byId = id => root.all().find(item => item.id === id);
  const rowButton = (id, action) => root.all().find(item => item.dataset.scenarioId === id).all().find(item => item.dataset.campaignAction === action);
  const press = element => { assert.ok(element, "Expected control exists"); assert.ok(!element.disabled, "Expected control enabled"); return element.listeners.click(); };
  const submit = () => root.all().find(item => item.tagName === "form").listeners.submit({ preventDefault() {} });
  return { editor, root, calls, saved, byId, rowButton, press, submit, setConfirm: value => { confirmResult = value; }, confirms: () => confirms };
}

test("campaign editor reorders shared references and submits optimistic revision without scenario definitions", async () => {
  const h = await harness();
  h.press(h.rowButton("two", "up"));
  await h.submit();
  assert.equal(h.calls[1].method, "PUT");
  assert.equal(h.calls[1].path, "/campaigns/test-campaign");
  assert.deepEqual(JSON.parse(h.calls[1].body), { name: "Evening adventures", description: null, coverArt: null, scenarioIds: ["two", "one"], revision: 4 });
  assert.equal(h.saved.length, 1);
  assert.equal(h.editor.canLeave(), true);
  assert.equal(h.confirms(), 0);
});

test("new campaigns can use any library scenario without campaign membership", async () => {
  const h = await harness(null);
  h.byId("campaign-name").value = "A new journey";
  h.press(h.root.all().find(item => item.dataset.addScenario === "three"));
  await h.submit();
  assert.equal(h.calls[1].path, "/campaigns");
  assert.equal(h.calls[1].method, "POST");
  assert.deepEqual(JSON.parse(h.calls[1].body).scenarioIds, ["three"]);
  assert.equal("revision" in JSON.parse(h.calls[1].body), false);
});

test("missing references stay visible and can be preserved or explicitly removed", async () => {
  const h = await harness(campaign({ scenarioIds: ["missing", "one"] }));
  assert.match(h.root.textContent, /Unavailable scenario/);
  await h.submit();
  assert.deepEqual(JSON.parse(h.calls[1].body).scenarioIds, ["missing", "one"]);
  h.press(h.rowButton("missing", "remove"));
  await h.submit();
  assert.deepEqual(JSON.parse(h.calls[2].body).scenarioIds, ["one"]);
  assert.equal(JSON.parse(h.calls[2].body).revision, 5);
});

test("save conflicts retain edits and dirty navigation protection", async () => {
  const h = await harness(campaign(), () => { throw new Error("Changed elsewhere. Reopen before saving."); });
  h.byId("campaign-name").value = "Unsaved edit";
  await h.submit();
  assert.match(h.byId("campaign-editor-status").textContent, /Changed elsewhere/);
  assert.equal(h.byId("campaign-name").value, "Unsaved edit");
  assert.equal(h.editor.canLeave(), false);
  assert.equal(h.confirms(), 1);
  assert.equal(h.saved.length, 0);
  assert.equal(h.byId("campaign-save").disabled, false);
});

test("deleting a campaign requires explicit confirmation and sends the stored revision", async () => {
  const h = await harness();
  h.press(h.byId("campaign-delete"));
  assert.equal(h.calls.length, 1);
  await h.press(h.byId("campaign-delete-confirm"));
  assert.equal(h.calls[1].method, "DELETE");
  assert.equal(h.calls[1].path, "/campaigns/test-campaign?revision=4");
  assert.deepEqual(h.saved, [null]);
});

test("campaign mutations prevent navigation until the request finishes", async () => {
  let resolve;
  const h = await harness(campaign(), () => new Promise(done => { resolve = done; }));
  h.byId("campaign-description").value = "An evening in the hills";
  const saving = h.submit();
  assert.equal(h.editor.canLeave(), false);
  assert.equal(h.byId("campaign-name").disabled, true);
  resolve(campaign({ description: "An evening in the hills", revision: 5 }));
  await saving;
  assert.equal(h.byId("campaign-name").disabled, false);
  assert.equal(h.editor.canLeave(), true);
});

test("scenario search only changes the add list, retaining campaign order", async () => {
  const h = await harness();
  const search = h.byId("campaign-scenario-search");
  search.value = "nothing matches"; search.listeners.input();
  assert.match(h.byId("campaign-library-scenarios").textContent, /No matching/);
  await h.submit();
  assert.deepEqual(JSON.parse(h.calls[1].body).scenarioIds, ["one", "two"]);
});

test("cover previews reject executable URLs and display failures gracefully", async () => {
  const h = await harness();
  const cover = h.byId("campaign-cover");
  cover.value = "javascript:alert(1)"; cover.listeners.input();
  assert.equal(h.root.all().filter(item => item.tagName === "img").length, 0);
  cover.value = "https://example.test/cover.webp"; cover.listeners.input();
  const image = h.root.all().find(item => item.tagName === "img");
  assert.equal(image.referrerPolicy, "no-referrer");
  image.listeners.error();
  assert.match(h.root.textContent, /Artwork unavailable/);
});

test("editing a bundled campaign sends its conflict token without mutating the original source", async () => {
  const h = await harness(campaign({source:"bundled",changeToken:"bundle-token"}));
  assert.match(h.byId("campaign-editor-status").textContent, /saving creates a local override/);
  h.byId("campaign-description").value = "Local experiment";
  await h.submit();
  assert.equal(JSON.parse(h.calls[1].body).changeToken, "bundle-token");
  assert.equal("source" in JSON.parse(h.calls[1].body), false);
});

test("campaign cover import stores PNG but only changes the unsaved cover field", async () => {
  const png = {type:"image/png",size:100};
  const h = await harness(campaign(), (path, options) => {
    assert.equal(path,"/covers"); assert.equal(options.body,png);
    assert.equal(options.headers["Content-Type"],"image/png");
    return {coverArt:"/api/library/covers/cover.png"};
  }, async () => png);
  h.byId("campaign-cover-file").files = [{type:"image/webp"}];
  await h.byId("campaign-cover-file").listeners.change();
  assert.equal(h.byId("campaign-cover").value,"/api/library/covers/cover.png");
  assert.equal(h.calls.length,2); assert.equal(h.saved.length,0);
  assert.equal(h.editor.canLeave(),false);
  assert.match(h.byId("campaign-editor-status").textContent,/Save campaign/);
});

test("campaign cover import failure retains previous cover and editable draft", async () => {
  const h = await harness(campaign({coverArt:"/art/previous.webp"}), undefined, async () => {throw new Error("Cannot decode image.");});
  h.byId("campaign-cover-file").files = [{type:"image/webp"}];
  await h.byId("campaign-cover-file").listeners.change();
  assert.equal(h.byId("campaign-cover").value,"/art/previous.webp");
  assert.equal(h.byId("campaign-name").disabled,false);
  assert.match(h.byId("campaign-editor-status").textContent,/Existing cover retained/);
});

test("campaign restoration requires confirmation and sends revision plus token", async () => {
  const h = await harness(campaign({source:"override",changeToken:"edited-token"}), () => campaign({source:"bundled",changeToken:"fresh-token"}));
  h.press(h.byId("campaign-restore")); assert.equal(h.calls.length,1);
  await h.press(h.byId("campaign-restore-confirm"));
  assert.equal(h.calls[1].path,"/campaigns/test-campaign/restore");
  assert.deepEqual(JSON.parse(h.calls[1].body),{revision:4,changeToken:"edited-token"});
  assert.equal(h.saved[0].source,"bundled");
});
