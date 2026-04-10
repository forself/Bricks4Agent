import { DefinitionAuthFormPage } from '../pages/DefinitionAuthFormPage.js';
import { DefinitionContentPage } from '../pages/DefinitionContentPage.js';
import { DefinitionListPage } from '../pages/DefinitionListPage.js';
import { materializeDataTableSurface } from './surfaces/DataTableSurface.js';
import { materializeEmbeddedActionFormSurface } from './surfaces/EmbeddedActionFormSurface.js';
import { materializeMessagePanelSurface } from './surfaces/MessagePanelSurface.js';
import { materializeSearchFormSurface } from './surfaces/SearchFormSurface.js';

const PAGE_KIND_TO_PAGE_CLASS = Object.freeze({
    resource_list_page: DefinitionListPage,
    content_page: DefinitionContentPage,
    auth_form_page: DefinitionAuthFormPage,
});

const SURFACE_MATERIALIZERS = Object.freeze({
    message_panel: materializeMessagePanelSurface,
    search_form: materializeSearchFormSurface,
    data_table: materializeDataTableSurface,
    embedded_action_form: materializeEmbeddedActionFormSurface,
});

function materializeRouteSurface(surface) {
    const materialize = SURFACE_MATERIALIZERS[surface?.surface_kind];
    if (!materialize) {
        throw new Error(`Unsupported surface_kind: ${surface?.surface_kind}`);
    }

    return materialize(surface);
}

export class FrontendDefinitionMaterializer {
    constructor(definition = {}) {
        this.definition = definition;
    }

    materialize() {
        const routes = {};

        for (const route of this.definition.routes || []) {
            const pageClass = PAGE_KIND_TO_PAGE_CLASS[route?.page_kind] || null;
            const surfaces = (route?.surfaces || []).map(materializeRouteSurface);

            routes[route.path] = {
                path: route.path,
                pageId: route.page_id || null,
                pageKind: route.page_kind || null,
                pageClass,
                title: route.title || '',
                access: route.access || null,
                state: route.state || {},
                dataSources: route.data_sources || {},
                surfaces,
            };
        }

        return {
            app: this.definition.app || {},
            auth: this.definition.auth || {},
            navigation: this.definition.navigation || { items: [] },
            sharedResources: this.definition.shared_resources || { enums: {}, messages: {} },
            routes,
        };
    }
}

export default FrontendDefinitionMaterializer;
