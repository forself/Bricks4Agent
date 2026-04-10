import {
    getSharedEnumLabel,
    getSharedEnumOptions,
} from '../definition/definition-store.js';

export const DEFAULT_CATEGORY_OPTIONS = [];

export const DEFAULT_STATUS_OPTIONS = [
    { value: 'active', label: 'Active' },
    { value: 'inactive', label: 'Inactive' }
];

let cachedCategoryOptions = null;
let categoryOptionsPromise = null;

function normalizeCategoryOptions(categories) {
    return Array.isArray(categories)
        ? categories
            .filter((category) => category && category.id > 0 && category.name)
            .map((category) => ({
                value: category.id,
                label: category.name
            }))
        : [];
}

export async function ensureCategoryOptions(api) {
    if (Array.isArray(cachedCategoryOptions)) {
        return cachedCategoryOptions.slice();
    }

    if (!categoryOptionsPromise) {
        categoryOptionsPromise = Promise.resolve()
            .then(async () => {
                if (!api?.get) {
                    cachedCategoryOptions = getSharedEnumOptions('category_options', DEFAULT_CATEGORY_OPTIONS);
                    return cachedCategoryOptions.slice();
                }

                const categories = await api.get('/shop/categories');
                cachedCategoryOptions = normalizeCategoryOptions(categories);
                return cachedCategoryOptions.slice();
            })
            .finally(() => {
                categoryOptionsPromise = null;
            });
    }

    return categoryOptionsPromise;
}

export function getCategoryOptions() {
    if (Array.isArray(cachedCategoryOptions)) {
        return cachedCategoryOptions.slice();
    }

    return getSharedEnumOptions('category_options', DEFAULT_CATEGORY_OPTIONS);
}

export function getStatusOptions() {
    return getSharedEnumOptions('status_options', DEFAULT_STATUS_OPTIONS);
}

export function getCategoryLabel(categoryId) {
    return getCategoryOptions().find((item) => String(item.value) === String(categoryId))?.label
        || getSharedEnumLabel('category_options', categoryId, `Category #${categoryId}`);
}

export function getStatusLabel(status) {
    return getSharedEnumLabel('status_options', status, status || 'Unknown');
}

export function __resetCategoryOptionsForTests() {
    cachedCategoryOptions = null;
    categoryOptionsPromise = null;
}
