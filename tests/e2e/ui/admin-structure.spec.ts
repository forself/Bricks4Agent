import { test, expect } from '@playwright/test';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const adminHtmlPath = path.resolve(__dirname, '../../../packages/csharp/broker/wwwroot/line-admin.html');

test.describe('Admin UI Structure', () => {
    test.beforeEach(async ({ page }) => {
        await page.goto(`file://${adminHtmlPath.replace(/\\/g, '/')}`);
    });

    test('has 7 navigation tabs', async ({ page }) => {
        const tabs = page.locator('.nav button[data-tab]');
        await expect(tabs).toHaveCount(7);
    });

    test('navigation tabs have correct data-tab values', async ({ page }) => {
        const expectedTabs = ['line', 'workflow', 'browser', 'deployment', 'delivery', 'alerts', 'tools'];
        for (const tabName of expectedTabs) {
            const tab = page.locator(`.nav button[data-tab="${tabName}"]`);
            await expect(tab).toBeAttached();
        }
    });

    test('has login screen with password input', async ({ page }) => {
        const loginScreen = page.locator('#login-screen');
        await expect(loginScreen).toBeAttached();
        const passwordInput = page.locator('#login-password[type="password"]');
        await expect(passwordInput).toBeAttached();
    });

    test('tab sections exist', async ({ page }) => {
        const expectedSections = ['tab-line', 'tab-workflow', 'tab-browser', 'tab-deployment', 'tab-delivery', 'tab-alerts', 'tab-tools'];
        for (const sectionId of expectedSections) {
            const section = page.locator(`section#${sectionId}, div#${sectionId}, [id="${sectionId}"]`);
            await expect(section).toBeAttached();
        }
    });
});
