const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
function harness(width = 3000, height = 1500) {
  let closed = false, drawn = false;
  const png = { size: 1024, type: 'image/png' };
  const canvas = { getContext: () => ({ drawImage: () => { drawn = true; } }), toBlob: (done, type) => { assert.equal(type, 'image/png'); done(png); } };
  const context = vm.createContext({document: { createElement: () => canvas }, createImageBitmap: async () => ({width,height,close: () => {closed=true;}})});
  vm.runInContext(fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/cover-upload.js'),'utf8').replace(/^export /gm,''),context);
  return { context, canvas, png, closed:()=>closed, drawn:()=>drawn, prepare: file=>context.prepareCover(file) };
}
test('cover import redraws supported formats as bounded PNG and releases the decoder', async () => {
  for (const type of ['image/png','image/jpeg','image/webp']) {
    const h=harness(); assert.equal(await h.prepare({type,size:2000}),h.png);
    assert.equal(h.canvas.width,2048); assert.equal(h.canvas.height,1024); assert.equal(h.drawn(),true); assert.equal(h.closed(),true);
  }
});
test('cover import rejects executable, oversized and undecodable images', async () => {
  const h=harness();
  await assert.rejects(h.prepare({type:'image/svg+xml',size:12}),/PNG, JPEG or WebP/);
  await assert.rejects(h.prepare({type:'image/png',size:13*1024*1024}),/12 MB/);
  h.context.createImageBitmap=async()=>{throw Error('decode failed');};
  await assert.rejects(h.prepare({type:'image/png',size:20}),/could not be decoded/);
});
test('cover import enforces dimensions and encoded size without leaking decoder resources', async () => {
  const h=harness(5000,3000); await assert.rejects(h.prepare({type:'image/png',size:20}),/4096/); assert.equal(h.closed(),true);
  const large=harness(); large.png.size=6*1024*1024;
  await assert.rejects(large.prepare({type:'image/png',size:20}),/5 MB/); assert.equal(large.closed(),true);
});
