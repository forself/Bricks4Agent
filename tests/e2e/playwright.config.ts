import { defineConfig } from '@playwright/test';

export default defineConfig({
    testDir: '.',
    timeout: 30000,
    use: {
        baseURL: 'http://127.0.0.1:5000',
        headless: true,
    },
    webServer: {
        command: 'dotnet run --project templates/spa/backend/SpaApi.csproj --no-launch-profile',
        cwd: '../..',
        url: 'http://127.0.0.1:5000/health',
        reuseExistingServer: true,
        env: {
            ASPNETCORE_URLS: 'http://127.0.0.1:5000',
            ASPNETCORE_ENVIRONMENT: 'Development',
            SeedData__AdminPassword: 'AdminProof123!',
            ConnectionStrings__DefaultConnection: 'Data Source=spa-proof-e2e.db'
        }
    },
    projects: [
        { name: 'chromium', use: { browserName: 'chromium' } },
    ],
});
