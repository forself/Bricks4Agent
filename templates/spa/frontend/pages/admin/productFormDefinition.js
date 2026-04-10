import { getCategoryOptions, getStatusOptions } from '../commerce.constants.js';

export function createProductFormDefinition() {
    return {
        name: 'AdminProductFormPage',
        type: 'form',
        description: 'Admin Product Form',
        components: [],
        services: [],
        fields: [
            {
                name: 'name',
                type: 'text',
                label: 'Product Name',
                required: true
            },
            {
                name: 'description',
                type: 'textarea',
                label: 'Description'
            },
            {
                name: 'price',
                type: 'number',
                label: 'Price',
                required: true
            },
            {
                name: 'stock',
                type: 'number',
                label: 'Stock',
                required: true
            },
            {
                name: 'categoryId',
                type: 'select',
                label: 'Category',
                required: true,
                options: getCategoryOptions()
            },
            {
                name: 'status',
                type: 'select',
                label: 'Status',
                required: true,
                options: getStatusOptions()
            }
        ],
        api: {
            get: '/api/admin/products',
            create: '/api/admin/products',
            update: '/api/admin/products'
        },
        behaviors: {
            fieldTriggers: {}
        },
        styles: {
            layout: 'single',
            theme: 'default'
        }
    };
}

export const productFormDefinition = createProductFormDefinition();

export function normalizeProductPayload(values = {}) {
    return {
        name: String(values.name || '').trim(),
        description: String(values.description || '').trim(),
        price: Number(values.price ?? 0),
        stock: Number(values.stock ?? 0),
        categoryId: Number(values.categoryId ?? 0),
        status: String(values.status || 'active')
    };
}
