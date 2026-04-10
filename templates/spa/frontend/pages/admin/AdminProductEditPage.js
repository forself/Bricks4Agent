import { DefinitionRuntimePage } from '../../runtime/DefinitionRuntimePage.js';
import { normalizeProductPayload, createProductFormDefinition } from './productFormDefinition.js';
import { ensureCategoryOptions } from '../commerce.constants.js';

export class AdminProductEditPage extends DefinitionRuntimePage {
    async onInit() {
        await ensureCategoryOptions(this.api);
        this.options.definitionOverride = createProductFormDefinition();
        await super.onInit();
    }

    async handleSave(values) {
        const payload = normalizeProductPayload(values);
        const productId = this.params?.id;

        this.showLoading();
        try {
            await this.api.put(`/admin/products/${productId}`, payload);
            this.navigate('/admin/products', {
                query: { flash: 'updated' }
            });
        } catch (error) {
            this.showMessage(error.message || '更新商品失敗', 'error');
            throw error;
        } finally {
            this.hideLoading();
        }
    }
}

AdminProductEditPage.pageId = 'admin-product-edit';
AdminProductEditPage.definition = createProductFormDefinition();

export default AdminProductEditPage;
