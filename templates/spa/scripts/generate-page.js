#!/usr/bin/env node
/**
 * SPA 頁面生成器
 *
 * 用法:
 *   node generate-page.js ProductList
 *   node generate-page.js products/ProductDetail --nested
 *   node generate-page.js orders/OrderList --with-api
 *
 * @module generate-page
 */

const fs = require('fs');
const path = require('path');
const readline = require('readline');

// ===== 配置 =====
const PAGES_DIR = path.join(__dirname, '..', 'frontend', 'pages');
const GENERATED_ROUTES_DIR = path.join(PAGES_DIR, 'generated');
const GENERATED_ROUTES_FILE = path.join(GENERATED_ROUTES_DIR, 'routes.generated.js');

// ===== 模板 =====

const PAGE_TEMPLATE = `/**
 * {{displayName}} - {{description}}
 *
 * @module {{className}}
 */

import { BasePage } from '{{coreImport}}';

export class {{className}} extends BasePage {
    async onInit() {
        this._data = {
            items: [],
            loading: true,
            error: null
        };

        await this._loadData();
    }

    async _loadData() {
        try {
            this._data.loading = true;

            // TODO: 替換為實際 API 呼叫
            // const items = await this.api.get('/{{apiPath}}');
            // this._data.items = items;

            // 模擬資料
            await new Promise(resolve => setTimeout(resolve, 300));
            this._data.items = [
                { id: 1, name: '範例項目 1' },
                { id: 2, name: '範例項目 2' },
                { id: 3, name: '範例項目 3' }
            ];

        } catch (error) {
            console.error('[{{className}}] 載入資料失敗:', error);
            this._data.error = error.message;
            this.showMessage('載入失敗', 'error');
        } finally {
            this._data.loading = false;
        }
    }

    template() {
        const { items, loading, error } = this._data;

        if (loading) {
            return \`
                <div class="{{cssClass}}">
                    <div class="loading-state">
                        <div class="loading-spinner"></div>
                        <p>載入中...</p>
                    </div>
                </div>
            \`;
        }

        if (error) {
            return \`
                <div class="{{cssClass}}">
                    <div class="error-state">
                        <h2>載入失敗</h2>
                        <p>\${this.esc(error)}</p>
                        <button class="btn btn-primary" id="btn-retry">重試</button>
                    </div>
                </div>
            \`;
        }

        return \`
            <div class="{{cssClass}}">
                <header class="page-header">
                    <h1>{{displayName}}</h1>
                    <p class="page-subtitle">{{description}}</p>
                </header>

                <div class="page-content">
                    <div class="list-container">
                        \${items.length === 0 ? \`
                            <div class="empty-state">
                                <p>目前沒有資料</p>
                            </div>
                        \` : \`
                            <ul class="item-list">
                                \${items.map(item => \`
                                    <li class="item" data-id="\${this.escAttr(item.id)}">
                                        <span class="item-name">\${this.esc(item.name)}</span>
                                        <div class="item-actions">
                                            <button class="btn btn-sm btn-edit" data-id="\${this.escAttr(item.id)}">
                                                編輯
                                            </button>
                                            <button class="btn btn-sm btn-danger btn-delete" data-id="\${this.escAttr(item.id)}">
                                                刪除
                                            </button>
                                        </div>
                                    </li>
                                \`).join('')}
                            </ul>
                        \`}
                    </div>
                </div>
            </div>
        \`;
    }

    events() {
        return {
            'click #btn-retry': 'onRetry',
            'click .btn-edit': 'onEdit',
            'click .btn-delete': 'onDelete'
        };
    }

    onRetry() {
        this._loadData();
    }

    onEdit(event, target) {
        const id = target.dataset.id;
        this.navigate(\`/{{routePath}}/\${id}/edit\`);
    }

    async onDelete(event, target) {
        const id = target.dataset.id;
        const item = this._data.items.find(i => i.id === parseInt(id));

        if (!confirm(\`確定要刪除「\${item?.name}」嗎？\`)) {
            return;
        }

        try {
            // await this.api.delete(\`/{{apiPath}}/\${id}\`);
            this._data.items = this._data.items.filter(i => i.id !== parseInt(id));
            this.showMessage('已刪除', 'success');
        } catch (error) {
            this.showMessage('刪除失敗', 'error');
        }
    }
}

export default {{className}};
`;

const DETAIL_PAGE_TEMPLATE = `/**
 * {{displayName}} - {{description}}
 *
 * @module {{className}}
 */

import { BasePage } from '{{coreImport}}';

export class {{className}} extends BasePage {
    async onInit() {
        this._data = {
            item: null,
            loading: true,
            error: null
        };

        await this._loadItem();
    }

    async _loadItem() {
        const id = this.params.id;

        try {
            this._data.loading = true;

            // TODO: 替換為實際 API 呼叫
            // const item = await this.api.get(\`/{{apiPath}}/\${id}\`);
            // this._data.item = item;

            // 模擬資料
            await new Promise(resolve => setTimeout(resolve, 200));
            this._data.item = {
                id: parseInt(id),
                name: \`項目 \${id}\`,
                description: '這是項目描述',
                createdAt: '2026-01-25'
            };

        } catch (error) {
            console.error('[{{className}}] 載入失敗:', error);
            this._data.error = error.message;
        } finally {
            this._data.loading = false;
        }
    }

    template() {
        const { item, loading, error } = this._data;

        if (loading) {
            return \`
                <div class="{{cssClass}}">
                    <div class="loading-state">
                        <div class="loading-spinner"></div>
                        <p>載入中...</p>
                    </div>
                </div>
            \`;
        }

        if (error || !item) {
            return \`
                <div class="{{cssClass}}">
                    <div class="error-state">
                        <h2>找不到資料</h2>
                        <a href="#/{{routePath}}" class="btn">返回列表</a>
                    </div>
                </div>
            \`;
        }

        return \`
            <div class="{{cssClass}}">
                <div class="page-back">
                    <a href="#/{{routePath}}" class="back-link">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <line x1="19" y1="12" x2="5" y2="12"/>
                            <polyline points="12 19 5 12 12 5"/>
                        </svg>
                        返回列表
                    </a>
                </div>

                <div class="detail-card">
                    <div class="detail-header">
                        <h2>\${this.esc(item.name)}</h2>
                    </div>

                    <div class="detail-body">
                        <div class="detail-section">
                            <h3>基本資訊</h3>
                            <div class="detail-grid">
                                <div class="detail-item">
                                    <label>ID</label>
                                    <span>\${this.esc(item.id)}</span>
                                </div>
                                <div class="detail-item">
                                    <label>描述</label>
                                    <span>\${this.esc(item.description || '-')}</span>
                                </div>
                                <div class="detail-item">
                                    <label>建立時間</label>
                                    <span>\${this.esc(item.createdAt)}</span>
                                </div>
                            </div>
                        </div>

                        <div class="detail-actions">
                            <button class="btn btn-primary" id="btn-edit">編輯</button>
                            <button class="btn btn-danger" id="btn-delete">刪除</button>
                        </div>
                    </div>
                </div>
            </div>
        \`;
    }

    events() {
        return {
            'click #btn-edit': 'onEdit',
            'click #btn-delete': 'onDelete'
        };
    }

    onEdit() {
        this.navigate(\`/{{routePath}}/\${this.params.id}/edit\`);
    }

    async onDelete() {
        if (!confirm('確定要刪除嗎？此操作無法復原。')) {
            return;
        }

        try {
            // await this.api.delete(\`/{{apiPath}}/\${this.params.id}\`);
            this.showMessage('已刪除', 'success');
            this.navigate('/{{routePath}}');
        } catch (error) {
            this.showMessage('刪除失敗', 'error');
        }
    }
}

export default {{className}};
`;

// ===== 工具函數 =====

function parseArgs() {
    const args = { flags: {} };
    const argv = process.argv.slice(2);

    for (let i = 0; i < argv.length; i++) {
        if (argv[i].startsWith('--')) {
            const key = argv[i].slice(2);
            args.flags[key] = true;
        } else if (!args.pageName) {
            args.pageName = argv[i];
        }
    }

    return args;
}

function toPascalCase(str) {
    return str
        .split(/[-_\/]/)
        .map(part => part.charAt(0).toUpperCase() + part.slice(1))
        .join('');
}

function toKebabCase(str) {
    return str
        .replace(/([a-z])([A-Z])/g, '$1-$2')
        .replace(/[\s_]+/g, '-')
        .toLowerCase();
}

function ensureGeneratedRoutesFile(routesFile = GENERATED_ROUTES_FILE) {
    if (!fs.existsSync(path.dirname(routesFile))) {
        fs.mkdirSync(path.dirname(routesFile), { recursive: true });
    }

    if (!fs.existsSync(routesFile)) {
        fs.writeFileSync(
            routesFile,
            'export const generatedRoutes = [\n];\n\nexport default generatedRoutes;\n',
            'utf8'
        );
    }
}

function buildGeneratedRouteRegistration(result, options = {}) {
    const relativeImport = path.relative(GENERATED_ROUTES_DIR, result.filePath).replace(/\\/g, '/');
    const importPath = relativeImport.startsWith('.') ? relativeImport : `./${relativeImport}`;
    const title = (options.title || result.className.replace(/Page$/, '')).replace(/'/g, "\\'");

    return {
        importStatement: `import ${result.className} from '${importPath}';`,
        routeEntry: `    {\n`
            + `        path: '${result.routePath}',\n`
            + `        component: ${result.className},\n`
            + `        meta: {\n`
            + `            title: '${title}',\n`
            + `            generated: true\n`
            + `        }\n`
            + `    }`
    };
}

function registerGeneratedRoute(result, options = {}, routesFile = GENERATED_ROUTES_FILE) {
    ensureGeneratedRoutesFile(routesFile);

    let content = fs.readFileSync(routesFile, 'utf8');
    const { importStatement, routeEntry } = buildGeneratedRouteRegistration(result, options);

    if (!content.includes(importStatement)) {
        const importMatches = [...content.matchAll(/^import .*;$/gm)];
        if (importMatches.length > 0) {
            const lastMatch = importMatches[importMatches.length - 1];
            const insertPos = lastMatch.index + lastMatch[0].length;
            content = content.slice(0, insertPos) + '\n' + importStatement + content.slice(insertPos);
        } else {
            content = importStatement + '\n\n' + content;
        }
    }

    const hasRoute = content.includes(`path: '${result.routePath}'`) || content.includes(`component: ${result.className}`);
    if (!hasRoute) {
        const arrayCloseIndex = content.lastIndexOf('];');
        if (arrayCloseIndex === -1) {
            throw new Error(`Invalid generated routes file: ${routesFile}`);
        }

        const hasExistingRoutes = content.slice(0, arrayCloseIndex).includes('path:');
        const insertion = `${hasExistingRoutes ? ',\n' : ''}${routeEntry}\n`;
        content = content.slice(0, arrayCloseIndex) + insertion + content.slice(arrayCloseIndex);
    }

    fs.writeFileSync(routesFile, content, 'utf8');
}

function generatePage(pageName, options = {}) {
    // 解析路徑
    const parts = pageName.split('/');
    const fileName = parts[parts.length - 1];
    const className = toPascalCase(fileName) + 'Page';
    const subDir = parts.length > 1 ? parts.slice(0, -1).join('/') : '';

    // 計算相對路徑
    const depth = subDir ? subDir.split('/').length + 1 : 1;
    const coreImport = '../'.repeat(depth) + 'core/BasePage.js';

    // 決定使用哪個模板
    const isDetail = fileName.toLowerCase().includes('detail') ||
                     fileName.toLowerCase().includes('view') ||
                     options.detail;
    const template = isDetail ? DETAIL_PAGE_TEMPLATE : PAGE_TEMPLATE;

    // 替換變數
    const routePath = subDir ? `${subDir}/${toKebabCase(fileName)}` : toKebabCase(fileName);
    const apiPath = subDir || toKebabCase(fileName);

    let content = template
        .replace(/\{\{className\}\}/g, className)
        .replace(/\{\{displayName\}\}/g, fileName)
        .replace(/\{\{description\}\}/g, `${fileName} 頁面`)
        .replace(/\{\{cssClass\}\}/g, toKebabCase(className))
        .replace(/\{\{coreImport\}\}/g, coreImport)
        .replace(/\{\{routePath\}\}/g, routePath)
        .replace(/\{\{apiPath\}\}/g, apiPath);

    // 決定輸出路徑
    const outputDir = subDir ? path.join(PAGES_DIR, subDir) : PAGES_DIR;
    const outputFile = path.join(outputDir, `${className}.js`);

    // 建立目錄
    if (!fs.existsSync(outputDir)) {
        fs.mkdirSync(outputDir, { recursive: true });
    }

    // 寫入檔案
    if (fs.existsSync(outputFile)) {
        console.error(`錯誤: 檔案已存在 ${outputFile}`);
        process.exit(1);
    }

    fs.writeFileSync(outputFile, content, 'utf8');

    return {
        className,
        filePath: outputFile,
        routePath: `/${routePath}`,
        importPath: subDir ? `./${subDir}/${className}.js` : `./${className}.js`
    };
}

// ===== 主程式 =====

async function main() {
    const args = parseArgs();

    if (!args.pageName) {
        console.log('');
        console.log('SPA 頁面生成器');
        console.log('');
        console.log('用法:');
        console.log('  node generate-page.js <頁面名稱> [選項]');
        console.log('');
        console.log('範例:');
        console.log('  node generate-page.js ProductList');
        console.log('  node generate-page.js products/ProductDetail');
        console.log('  node generate-page.js orders/OrderCreate --detail');
        console.log('');
        console.log('選項:');
        console.log('  --detail    使用詳情頁模板');
        console.log('');
        process.exit(0);
    }

    console.log('');
    console.log('正在生成頁面...');
    console.log('');

    const result = generatePage(args.pageName, {
        detail: args.flags.detail
    });

    if (!args.flags['no-register']) {
        registerGeneratedRoute(result, {
            title: args.pageName.split('/').pop()
        });
        console.log(`Auto-registered route: ${result.routePath}`);
        console.log(`Updated: ${GENERATED_ROUTES_FILE}`);
        console.log('');
    }

    console.log(`✓ 已建立: ${result.filePath}`);
    console.log('');
    console.log('下一步:');
    console.log('');
    console.log('1. 在 routes.js 中加入路由:');
    console.log('');
    console.log(`   import { ${result.className} } from '${result.importPath}';`);
    console.log('');
    console.log(`   { path: '${result.routePath}', component: ${result.className} }`);
    console.log('');
}

main().catch(console.error);
