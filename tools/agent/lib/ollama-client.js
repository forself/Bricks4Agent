'use strict';

/**
 * 向後相容包裝：re-export OllamaProvider 為 OllamaClient
 *
 * 新程式碼應直接使用：
 *   const { createProvider } = require('./providers/provider-factory');
 *   const provider = createProvider({ provider: 'ollama' });
 */
const { OllamaProvider } = require('./providers/ollama-provider');

// 保持舊 API 相容
const OllamaClient = OllamaProvider;

// supportsToolCalling 函式也移至 OllamaProvider.prototype
function supportsToolCalling(modelName) {
    const provider = new OllamaProvider();
    return provider.supportsToolCalling(modelName);
}

module.exports = { OllamaClient, supportsToolCalling };
