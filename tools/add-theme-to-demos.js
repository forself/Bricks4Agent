/**
 * 為所有 demo.html 加入 theme.css 引用
 * 在 <head> 區塊結束前插入 <link rel="stylesheet" href="RELATIVE_PATH/theme.css">
 */
const fs = require('fs');
const path = require('path');

const uiRoot = path.join(__dirname, '..', 'packages', 'javascript', 'browser', 'ui_components');
const themePath = path.join(uiRoot, 'theme.css');

function findDemoFiles(dir) {
  const results = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...findDemoFiles(full));
    } else if (entry.name === 'demo.html') {
      results.push(full);
    }
  }
  return results;
}

const demos = findDemoFiles(uiRoot);
console.log(`找到 ${demos.length} 個 demo.html 檔案\n`);

let modified = 0;
let skipped = 0;

for (const demoPath of demos) {
  const relDisplay = path.relative(uiRoot, demoPath).replace(/\\/g, '/');
  let content = fs.readFileSync(demoPath, 'utf-8');

  if (content.includes('theme.css')) {
    console.log(`[跳過] ${relDisplay} (已有 theme.css 引用)`);
    skipped++;
    continue;
  }

  // 計算從 demo.html 所在目錄到 uiRoot 的相對路徑
  const demoDir = path.dirname(demoPath);
  const relToRoot = path.relative(demoDir, uiRoot).replace(/\\/g, '/');
  const linkTag = `    <link rel="stylesheet" href="${relToRoot}/theme.css">`;

  // 在 </head> 前插入
  if (content.includes('</head>')) {
    content = content.replace('</head>', `${linkTag}\n</head>`);
    fs.writeFileSync(demoPath, content, 'utf-8');
    console.log(`[修改] ${relDisplay} → ${relToRoot}/theme.css`);
    modified++;
  } else {
    console.log(`[警告] ${relDisplay} 找不到 </head> 標籤`);
  }
}

console.log(`\n完成！修改: ${modified}, 跳過: ${skipped}, 總計: ${demos.length}`);
