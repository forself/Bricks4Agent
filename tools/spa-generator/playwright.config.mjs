/**
 * Playwright config for SPA Generator UI tests.
 *
 * The static server is the preferred frontend entry for automation.
 */
import { defineConfig } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export default defineConfig({
    testDir: './tests',
    timeout: 30000,
    retries: 0,
    use: {
        baseURL: 'http://localhost:3080',
        headless: true,
        viewport: { width: 1280, height: 800 },
        screenshot: 'only-on-failure',
        trace: 'retain-on-failure'
    },
    webServer: {
        command: 'dotnet run --project ../static-server/StaticServer.csproj -- ./frontend 3080',
        port: 3080,
        reuseExistingServer: true,
        cwd: __dirname
    }
});
