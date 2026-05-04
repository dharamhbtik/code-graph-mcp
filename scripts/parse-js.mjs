// scripts/parse-js.mjs
// Called by NodeProcessRunner via stdin/stdout JSON protocol
// Requires: npm install -g tree-sitter tree-sitter-javascript tree-sitter-typescript

import { readFileSync } from "fs";

let inputBuf = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", (d) => { inputBuf += d; });
process.stdin.on("end", () => {
  try {
    const { filePath } = JSON.parse(inputBuf);
    const source = readFileSync(filePath, "utf8");
    const lines  = source.split("\n");

    // Lightweight regex-based extraction (no native tree-sitter binding required)
    const nodes = [];
    const edges = [];
    const fileId = hashId(filePath + "::" + filePath);
    nodes.push({ id: fileId, kind: "File", name: filePath.split("/").at(-1),
                 fullName: filePath, filePath, language: "JavaScript",
                 startLine: 1, endLine: lines.length });

    // Functions
    const fnRe = /^(?:export\s+)?(?:async\s+)?function\s+(\w+)/gm;
    let m;
    while ((m = fnRe.exec(source)) !== null) {
      const name = m[1];
      const line = lineOf(source, m.index);
      const id   = hashId(filePath + "::" + name);
      nodes.push({ id, kind: "Function", name, fullName: `${filePath}::${name}`,
                   filePath, language: "JavaScript", startLine: line, endLine: line });
      edges.push({ id: hashId(fileId + id + "Contains"),
                   sourceId: fileId, targetId: id, kind: "Contains", weight: 1 });
    }

    // Classes
    const classRe = /^(?:export\s+)?class\s+(\w+)(?:\s+extends\s+(\w+))?/gm;
    while ((m = classRe.exec(source)) !== null) {
      const name = m[1];
      const base = m[2];
      const line = lineOf(source, m.index);
      const id   = hashId(filePath + "::" + name);
      const kind = detectAngularKind(source, m.index) ?? "Class";
      nodes.push({ id, kind, name, fullName: `${filePath}::${name}`,
                   filePath, language: kind === "Class" ? "JavaScript" : "Angular",
                   startLine: line, endLine: line });
      edges.push({ id: hashId(fileId + id + "Contains"),
                   sourceId: fileId, targetId: id, kind: "Contains", weight: 1 });
      if (base) {
        const baseId = hashId(filePath + "::" + base);
        edges.push({ id: hashId(id + baseId + "Inherits"),
                     sourceId: id, targetId: baseId, kind: "Inherits", weight: 1 });
      }
    }

    // Imports
    const importRe = /^import\s+.+\s+from\s+['"](.+)['"]/gm;
    while ((m = importRe.exec(source)) !== null) {
      const modulePath = m[1];
      const moduleId   = hashId(modulePath + "::" + modulePath);
      edges.push({ id: hashId(fileId + moduleId + "Imports"),
                   sourceId: fileId, targetId: moduleId, kind: "Imports", weight: 1 });
    }

    process.stdout.write(JSON.stringify({ nodes, edges }));
  } catch (err) {
    process.stdout.write(JSON.stringify({ nodes: [], edges: [], error: err.message }));
  }
});

function lineOf(source, index) {
  return source.slice(0, index).split("\n").length;
}

function hashId(str) {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    hash = ((hash << 5) - hash + str.charCodeAt(i)) | 0;
  }
  return Math.abs(hash).toString(16).padStart(16, "0");
}

function detectAngularKind(source, classIndex) {
  const before = source.slice(Math.max(0, classIndex - 200), classIndex);
  if (/@Component/.test(before))  return "Component";
  if (/@Injectable/.test(before)) return "Injectable";
  if (/@NgModule/.test(before))   return "NgModule";
  return null;
}
