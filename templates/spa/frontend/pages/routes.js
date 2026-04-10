import { HomePage } from './HomePage.js';
import { LoginPage } from './LoginPage.js';
import { RegisterPage } from './RegisterPage.js';
import { ProductListPage } from './products/ProductListPage.js';
import { OrderListPage } from './orders/OrderListPage.js';
import { AdminProductPage } from './admin/AdminProductPage.js';
import { AdminProductCreatePage } from './admin/AdminProductCreatePage.js';
import { AdminProductEditPage } from './admin/AdminProductEditPage.js';
import { generatedRoutes } from './generated/routes.generated.js';
import { FrontendDefinitionMaterializer } from '../runtime/materializers/FrontendDefinitionMaterializer.js';
import { getRouteTitleForPath } from '../definition/definition-store.js';

const PAGE_COMPONENTS = {
    home: HomePage,
    login: LoginPage,
    register: RegisterPage,
    products: ProductListPage,
    orders: OrderListPage,
    admin_products: AdminProductPage,
    admin_product_create: AdminProductCreatePage,
    admin_product_edit: AdminProductEditPage,
};

function buildRouteMeta(route, materializedRoute) {
    const access = route.access || {};
    return {
        title: route.title || materializedRoute?.title || getRouteTitleForPath(route.path) || route.page_id,
        requiresAuth: access.requiresAuth ?? false,
        requiresAdmin: access.requiresAdmin ?? false,
        definition: materializedRoute || null,
    };
}

export function createRoutes(frontendDefinition = null, options = {}) {
    const includeGeneratedRoutes = options.includeGeneratedRoutes !== false;

    if (!frontendDefinition || !Array.isArray(frontendDefinition.routes)) {
        return includeGeneratedRoutes ? generatedRoutes.slice() : [];
    }

    const materialized = new FrontendDefinitionMaterializer(frontendDefinition).materialize();
    const routes = [];

    for (const route of frontendDefinition.routes) {
        const component = PAGE_COMPONENTS[route.page_id];
        if (!component) {
            throw new Error(`Unsupported page_id: ${route.page_id} for route ${route.path}`);
        }

        routes.push({
            path: route.path,
            component,
            exact: route.exact ?? !route.path.includes(':'),
            meta: buildRouteMeta(route, materialized.routes[route.path] || null),
        });
    }

    return includeGeneratedRoutes ? routes.concat(generatedRoutes) : routes;
}

export default createRoutes;
