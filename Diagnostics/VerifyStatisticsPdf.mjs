// Usage: node Diagnostics/VerifyStatisticsPdf.mjs <absolute pdfjs-dist/legacy/build/pdf.mjs> [PDF directory]
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';

const { getDocument, OPS } = await import(pathToFileURL(path.resolve(process.argv[2])).href);
const folder = process.argv[3] ?? 'obj/PdfSmoke/previews';
let pagesChecked = 0;
for (const name of fs.readdirSync(folder).filter(name => name.endsWith('.pdf'))) {
  const data = fs.readFileSync(path.join(folder, name));
  assert(data.length < 2_000_000, `${name}: compact fonts and logo`);
  const pdf = await getDocument({ data: new Uint8Array(data), disableFontFace: true }).promise;
  const contents = [];
  let textOperations = 0, vectorOperations = 0;
  for (let i = 1; i <= pdf.numPages; i++) {
    const page = await pdf.getPage(i);
    const text = await page.getTextContent();
    const joined = text.items.map(item => item.str).join(' ').replace(/\p{Cf}/gu, '');
    assert(joined.includes('صفحة'), `${name}, page ${i}: searchable footer`);
    assert(joined.length > 20, `${name}, page ${i}: extractable text`);
    const operations = await page.getOperatorList();
    textOperations += operations.fnArray.filter(op => op === OPS.showText).length;
    vectorOperations += operations.fnArray.filter(op => op === OPS.constructPath).length;
    contents.push(joined); pagesChecked++;
  }
  assert(contents[0].includes('نظرة عامة'), `${name}: Arabic title search`);
  assert(contents[0].includes('المبالغ المحصلة'), `${name}: Arabic whole-word search`);
  assert(contents[0].includes('المقارنة مع الفترة السابقة'), `${name}: comparison on the first page`);
  assert(textOperations > 100 && vectorOperations > 20, `${name}: native text and vector graphics`);
  if (name.includes('long')) assert(contents.join(' ').includes('نهاية التفاصيل الكاملة'), 'Long description end remains searchable');
  console.log(`${name}: ${pdf.numPages} pages, Arabic search and vector graphics verified`);
  await pdf.destroy();
}
console.log(`PASS: ${pagesChecked} PDF pages verified with PDF.js.`);
