import { DefinitionRuntimePage } from '../../runtime/DefinitionRuntimePage.js';

export class DefinitionRuntimeSmokePage extends DefinitionRuntimePage {}

DefinitionRuntimeSmokePage.pageId = 'definition-runtime-smoke';
DefinitionRuntimeSmokePage.definition = {
    name: 'DefinitionRuntimeSmokePage',
    type: 'form',
    description: 'Definition Runtime Smoke',
    components: ['DatePicker', 'ColorPicker'],
    services: [],
    fields: [
        {
            name: 'name',
            type: 'text',
            label: 'Name',
            required: true
        },
        {
            name: 'startDate',
            type: 'date',
            label: 'Start Date',
            default: 'today'
        },
        {
            name: 'favoriteColor',
            type: 'color',
            label: 'Favorite Color',
            default: '#336699'
        },
        {
            name: 'isActive',
            type: 'checkbox',
            label: 'Active',
            default: true
        }
    ],
    api: {
        list: '/api/users',
        get: '/api/users',
        create: '/api/users',
        update: '/api/users',
        delete: '/api/users'
    },
    behaviors: {
        fieldTriggers: {}
    },
    styles: {
        layout: 'single',
        theme: 'default'
    }
};

export default DefinitionRuntimeSmokePage;
