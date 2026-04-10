import { DefinitionRuntimePage } from '../../runtime/DefinitionRuntimePage.js';
import { normalizeProductPayload, createProductFormDefinition } from './productFormDefinition.js';
import { ensureCategoryOptions } from '../commerce.constants.js';

export class AdminProductCreatePage extends DefinitionRuntimePage {
    async onInit() {
        await ensureCategoryOptions(this.api);
        this.options.definitionOverride = createProductFormDefinition();
        await super.onInit();
    }

    async handleSave(values) {
        const payload = normalizeProductPayload(values);

        this.showLoading();
        try {
            await this.api.post('/admin/products', payload);
            this.navigate('/admin/products', {
                query: { flash: 'created' }
            });
        } catch (error) {
            this.showMessage(error.message || '建立商品失敗', 'error');
            throw error;
        } finally {
            this.hideLoading();
        }
    }
}

AdminProductCreatePage.pageId = 'admin-product-create';
AdminProductCreatePage.definition = createProductFormDefinition();

export default AdminProductCreatePage;
