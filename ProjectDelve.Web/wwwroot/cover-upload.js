// Imported artwork is decoded and redrawn, never uploaded as executable/source metadata.
export async function prepareCover(file) {
  if (!file || !['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) throw new Error('Choose a PNG, JPEG or WebP image.');
  if (file.size > 12 * 1024 * 1024) throw new Error('Choose an image smaller than 12 MB.');
  let bitmap;
  try { bitmap = await createImageBitmap(file); }
  catch { throw new Error('This image could not be decoded. Choose a valid PNG, JPEG or WebP image.'); }
  try {
    if (!bitmap.width || !bitmap.height || bitmap.width > 4096 || bitmap.height > 4096 || bitmap.width * bitmap.height > 16_777_216)
      throw new Error('Cover images must be at most 4096 × 4096 pixels.');
    const scale = Math.min(1, 2048 / Math.max(bitmap.width, bitmap.height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(bitmap.width * scale)); canvas.height = Math.max(1, Math.round(bitmap.height * scale));
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Image preparation is unavailable in this browser.');
    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    const png = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
    if (!png || png.size > 5 * 1024 * 1024) throw new Error('Prepared image exceeds 5 MB. Choose a smaller image.');
    return png;
  } finally { bitmap.close(); }
}
