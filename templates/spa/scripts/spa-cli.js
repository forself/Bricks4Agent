#!/usr/bin/env node
/**
 * SPA CLI - 統一命令列工具
 *
 * 用法:
 *   spa new                     - 建立新專案
 *   spa page <name>             - 生成頁面
 *   spa api <entity>            - 生成 API
 *   spa feature <name>          - 生成完整功能 (頁面 + API)
 *
 * @module spa-cli
 */

const { spawn } = require('child_process');
const path = require('path');

const SCRIPTS_DIR = __dirname;

const commands = {
    'new': {
        script: 'create-project.js',
        description: '建立新的 SPA 專案'
    },
    'create': {
        script: 'create-project.js',
        description: '建立新的 SPA 專案 (同 new)'
    },
    'page': {
        script: 'generate-page.js',
        description: '生成前端頁面'
    },
    'api': {
        script: 'generate-api.js',
        description: '生成後端 API'
    },
    'feature': {
        handler: generateFeature,
        description: '生成完整功能 (前端頁面 + 後端 API)'
    }
};

function showHelp() {
    console.log('');
    console.log('╔════════════════════════════════════════╗');
    console.log('║            SPA CLI v1.0.0              ║');
    console.log('╚════════════════════════════════════════╝');
    console.log('');
    console.log('用法: node spa-cli.js <命令> [選項]');
    console.log('');
    console.log('命令:');
    console.log('');

    for (const [name, cmd] of Object.entries(commands)) {
        console.log(`  ${name.padEnd(12)} ${cmd.description}`);
    }

    console.log('');
    console.log('範例:');
    console.log('');
    console.log('  node spa-cli.js new');
    console.log('  node spa-cli.js page ProductList');
    console.log('  node spa-cli.js api Product --fields "Name:string,Price:decimal"');
    console.log('  node spa-cli.js feature Order --fields "CustomerId:int,Total:decimal"');
    console.log('');
}

function runScript(scriptName, args = []) {
    return new Promise((resolve, reject) => {
        const scriptPath = path.join(SCRIPTS_DIR, scriptName);
        const proc = spawn('node', [scriptPath, ...args], {
            stdio: 'inherit',
            cwd: process.cwd()
        });

        proc.on('close', code => {
            if (code === 0) resolve();
            else reject(new Error(`Script exited with code ${code}`));
        });

        proc.on('error', reject);
    });
}

async function generateFeature() {
    const args = process.argv.slice(3);

    if (args.length === 0 || args[0].startsWith('-')) {
        console.log('');
        console.log('用法: node spa-cli.js feature <功能名稱> [選項]');
        console.log('');
        console.log('範例:');
        console.log('  node spa-cli.js feature Product');
        console.log('  node spa-cli.js feature Order --fields "CustomerId:int,Total:decimal"');
        console.log('');
        return;
    }

    const featureName = args[0];
    const restArgs = args.slice(1);

    console.log('');
    console.log(`正在生成 ${featureName} 功能...`);
    console.log('');

    // 生成 API
    console.log('═══ 後端 API ═══');
    await runScript('generate-api.js', [featureName, ...restArgs]);

    console.log('');

    // 生成列表頁面
    console.log('═══ 前端頁面 ═══');
    const pluralName = featureName.endsWith('s') ? featureName : featureName + 's';
    const folderName = featureName.toLowerCase() + 's';

    await runScript('generate-page.js', [`${folderName}/${featureName}List`]);
    await runScript('generate-page.js', [`${folderName}/${featureName}Detail`, '--detail']);

    console.log('');
    console.log('═══ 完成 ═══');
    console.log('');
    console.log(`已生成 ${featureName} 功能的完整程式碼。`);
    console.log('');
    console.log('請按照上述說明完成以下步驟:');
    console.log('1. 更新 AppDbContext.cs');
    console.log('2. 更新 Program.cs (服務註冊 + API 端點)');
    console.log('3. 更新 routes.js (前端路由)');
    console.log('');
}

async function main() {
    const command = process.argv[2];

    if (!command || command === 'help' || command === '--help' || command === '-h') {
        showHelp();
        return;
    }

    const cmd = commands[command];

    if (!cmd) {
        console.error(`錯誤: 未知命令 '${command}'`);
        console.log('');
        console.log('使用 --help 查看可用命令');
        process.exit(1);
    }

    try {
        if (cmd.handler) {
            await cmd.handler();
        } else if (cmd.script) {
            const args = process.argv.slice(3);
            await runScript(cmd.script, args);
        }
    } catch (error) {
        console.error('錯誤:', error.message);
        process.exit(1);
    }
}

main();
