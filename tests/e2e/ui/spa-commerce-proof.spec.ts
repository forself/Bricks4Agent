import { test, expect } from '@playwright/test';

test.describe('SPA Commerce Proof', () => {
    test('supports membership commerce flow with frontend, backend, persistence, and admin', async ({ page }) => {
        const uniqueId = Date.now().toString();
        const customer = {
            name: `Proof Customer ${uniqueId}`,
            email: `proof-customer-${uniqueId}@example.com`,
            password: 'ProofPass123!'
        };
        const adminProductName = `Admin Created Product ${uniqueId}`;

        await page.goto('/');
        await expect(page.locator('.logo-text')).toHaveText('Bricks Commerce');
        await expect(page.locator('.logo-subtext')).toHaveText('Frontend definition bootstrap');
        await expect(page.locator('.sidebar-nav [data-path="/products"]')).toBeHidden();
        await expect(page.locator('.sidebar-nav [data-path="/orders"]')).toBeHidden();
        await expect(page.locator('.sidebar-nav [data-path="/admin/products"]')).toBeHidden();
        await expect(page.locator('.sidebar-nav [data-path="/register"]')).toBeVisible();
        await expect(page.locator('.sidebar-nav [data-path="/login"]')).toBeVisible();
        await expect(page.locator('.header-breadcrumb')).toHaveText('Home');

        await page.goto('/#/register');
        await page.locator('#name').fill(customer.name);
        await page.locator('#email').fill(customer.email);
        await page.locator('#password').fill(customer.password);
        await page.locator('#confirmPassword').fill(customer.password);
        await page.locator('#register-form button[type="submit"]').click();

        await page.goto('/#/login');
        await page.locator('#email').fill(customer.email);
        await page.locator('#password').fill(customer.password);
        await page.locator('#login-form button[type="submit"]').click();

        await expect.poll(async () => page.evaluate(() => location.hash)).toBe('#/');
        await expect(page.locator('.header-breadcrumb')).toHaveText('Home');
        await expect(page.locator('body')).toContainText(customer.name);

        await page.goto('/#/products');
        await expect.poll(async () => page.evaluate(() => location.hash)).toBe('#/products');
        await expect(page.locator('.header-breadcrumb')).toHaveText('Products');
        const firstPurchaseButton = page.locator('.js-buy-product').first();
        await expect(firstPurchaseButton).toBeVisible();
        await firstPurchaseButton.click();
        await page.locator('#shippingAddress').fill('Taipei Proof Road 1');
        await page.locator('#orderNote').fill('Please process proof order');
        await page.locator('#order-form button[type="submit"]').click();
        await expect(page.locator('#order-form')).toHaveCount(0);

        await page.goto('/#/orders');
        await expect.poll(async () => page.evaluate(() => location.hash)).toBe('#/orders');
        await expect(page.locator('.header-breadcrumb')).toHaveText('Orders');
        await expect(page.locator('body')).toContainText('Taipei Proof Road 1');

        await page.locator('[data-logout]').click();
        await expect.poll(async () => page.evaluate(() => location.hash)).toBe('#/login');
        await expect(page.locator('.sidebar-nav [data-path="/register"]')).toBeVisible();
        await expect(page.locator('.sidebar-nav [data-path="/login"]')).toBeVisible();
        await expect(page.locator('.sidebar-nav [data-path="/admin/products"]')).toBeHidden();

        await page.locator('#email').fill('admin@example.com');
        await page.locator('#password').fill('AdminProof123!');
        await page.locator('#login-form button[type="submit"]').click();

        await expect.poll(async () => page.evaluate(() => location.hash)).toBe('#/');
        await expect(page.locator('body')).toContainText('Admin');
        await expect(page.locator('.sidebar-nav [data-path="/products"]')).toBeVisible();
        await expect(page.locator('.sidebar-nav [data-path="/orders"]')).toBeVisible();
        await expect(page.locator('.sidebar-nav [data-path="/admin/products"]')).toBeVisible();
        await expect(page.locator('.sidebar-nav [data-path="/register"]')).toBeHidden();
        await expect(page.locator('.sidebar-nav [data-path="/login"]')).toBeHidden();

        await page.goto('/#/admin/products');
        await expect.poll(async () => page.evaluate(() => location.hash)).toBe('#/admin/products');
        await expect(page.locator('.header-breadcrumb')).toHaveText('Admin Products');

        await page.goto('/#/admin/products/create');
        await expect.poll(async () => page.evaluate(() => location.hash)).toBe('#/admin/products/create');
        await expect(page.locator('.header-breadcrumb')).toHaveText('Create Product');
        await page.locator('[data-field="name"] input').fill(adminProductName);
        await page.locator('[data-field="description"] textarea').fill('Created from proof admin page.');
        await page.locator('[data-field="price"] input').fill('880');
        await page.locator('[data-field="stock"] input').fill('15');
        await page.locator('[data-field="categoryId"] .dropdown__input').click();
        await page.locator('[data-field="categoryId"] .dropdown__option').filter({ hasText: 'Digital Goods' }).click();
        await page.keyboard.press('Escape');
        await page.locator('[data-field="status"] .dropdown__input').click();
        await page.locator('[data-field="status"] .dropdown__option[data-value="active"]').click();
        await page.keyboard.press('Escape');
        await page.locator('.dynamic-form__footer button').last().click();

        await expect.poll(async () => page.evaluate(() => location.hash)).toContain('#/admin/products');
        await expect(page.locator('body')).toContainText(adminProductName);
    });
});
