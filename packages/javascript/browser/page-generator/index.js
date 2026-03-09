/**
 * Page Generator Module
 *
 * 頁面生成器模組 - 根據頁面定義生成實際頁面
 *
 * @module page-generator
 */

export {
    FieldTypes,
    PageTypes,
    ComponentMapping,
    AvailableComponents,
    validateDefinition,
    inferComponents,
    createDefaultDefinition
} from './PageDefinition.js';

export {
    PageGenerator,
    ComponentPaths,
    FieldRenderers
} from './PageGenerator.js';

// 格式轉換器
export { PageDefinitionAdapter } from './PageDefinitionAdapter.js';

// 動態頁面渲染引擎（Layer 2-4）
export { TriggerEngine } from './TriggerEngine.js';
export { FieldResolver } from './FieldResolver.js';
export { DynamicFormRenderer } from './DynamicFormRenderer.js';
export { DynamicDetailRenderer } from './DynamicDetailRenderer.js';
export { DynamicListRenderer } from './DynamicListRenderer.js';
export { DynamicPageRenderer } from './DynamicPageRenderer.js';
