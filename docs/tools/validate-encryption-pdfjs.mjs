// Opens every encrypted document of the validation corpus with pdf.js, the reader in Firefox and in most
// browser-based viewers, which shares no code with qpdf, MuPDF or pypdf.
//
//   npm install --prefix <dir> pdfjs-dist@6.4.299
//   node docs/tools/validate-encryption-pdfjs.mjs <corpus-dir> <dir>/node_modules/pdfjs-dist/legacy/build/pdf.mjs
//
// For each document: the user password opens it and the first line drawn on page 1 is in its text; the owner
// password opens it; a wrong password is refused with pdf.js's PasswordException; and a document that has a
// user password is not opened without one. validate-encryption.py asks qpdf, MuPDF and pypdf the same.

import {readFile} from "node:fs/promises";
import path from "node:path";
import {pathToFileURL} from "node:url";

const [corpus, pdfjsPath] = process.argv.slice(2);

if (!corpus || !pdfjsPath) {
    console.log("usage: node validate-encryption-pdfjs.mjs <corpus-dir> <path to pdfjs-dist/legacy/build/pdf.mjs>");
    process.exit(2);
}

const pdfjs = await import(pathToFileURL(path.resolve(pdfjsPath)).href);
const manifest = JSON.parse(await readFile(path.join(corpus, "manifest.json"), "utf8"));

async function open(file, password) {
    const data = new Uint8Array(await readFile(path.join(corpus, file)));
    const task = pdfjs.getDocument({data, password, isEvalSupported: false, verbosity: 0});

    // The loading task owns the document in pdf.js 6, which has no PDFDocumentProxy.destroy(): destroying
    // the task is what releases both.
    try {
        return {task, document: await task.promise};
    } catch (error) {
        await task.destroy();
        throw error;
    }
}

async function refuses(file, password) {
    try {
        const {task} = await open(file, password);
        await task.destroy();
        return false;
    } catch (error) {
        return error?.name === "PasswordException";
    }
}

const normalise = (text) => text.normalize("NFC").replace(/\s+/g, " ").trim();
let failures = 0;

for (const entry of manifest.encryption) {
    const problems = [];

    try {
        const {task, document} = await open(entry.file, entry.userPassword);
        const page = await document.getPage(1);
        const content = await page.getTextContent();
        const text = normalise(content.items.map((item) => item.str + (item.hasEOL ? "\n" : "")).join(""));
        const first = normalise(entry.pages[0][0]);

        if (!text.includes(first)) {
            problems.push(`page 1 text does not contain ${JSON.stringify(first)}`);
        }

        await task.destroy();
    } catch (error) {
        problems.push(`the user password did not open it: ${error?.name}: ${error?.message}`);
    }

    try {
        const {task} = await open(entry.file, entry.ownerPassword);
        await task.destroy();
    } catch (error) {
        problems.push(`the owner password did not open it: ${error?.name}: ${error?.message}`);
    }

    if (!(await refuses(entry.file, "not-the-password")) && entry.userPassword) {
        problems.push("a wrong password opened it");
    }

    if (entry.userPassword && !(await refuses(entry.file, undefined))) {
        problems.push("it opened without a password");
    }

    failures += problems.length;
    console.log(`== ${entry.file}`);
    console.log(`  pdf.js: ${problems.length === 0 ? "passwords and text agree" : "FAILED"}`);

    for (const problem of problems) {
        console.log(`    ${problem}`);
    }
}

console.log();
console.log(`pdf.js: ${failures === 0 ? "every document opened as expected" : `${failures} failure(s)`}`);
process.exit(failures === 0 ? 0 : 1);
