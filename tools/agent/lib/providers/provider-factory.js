'use strict';

const { OllamaProvider } = require('./ollama-provider');
const { OpenAIProvider } = require('./openai-provider');

/**
 * Provider 別名對應表
 *
 * 每個別名定義：
 *   type      - 實際使用的 provider class ('ollama' | 'openai')
 *   host      - 預設 API 端點
 *   envKey    - 環境變數名稱（自動取得 API key）
 */
const PROVIDER_ALIASES = {
    ollama: {
        type: 'ollama',
        host: 'http://localhost:11434',
        envKey: null,
    },
    openai: {
        type: 'openai',
        host: 'https://api.openai.com',
        envKey: 'OPENAI_API_KEY',
        apiFormat: 'responses',  // Responses API（GPT-5 系列）
    },
    gemini: {
        type: 'openai',
        host: 'https://generativelanguage.googleapis.com/v1beta/openai',
        envKey: 'GEMINI_API_KEY',
    },
    deepseek: {
        type: 'openai',
        host: 'https://api.deepseek.com',
        envKey: 'DEEPSEEK_API_KEY',
    },
    groq: {
        type: 'openai',
        host: 'https://api.groq.com/openai',
        envKey: 'GROQ_API_KEY',
    },
    mistral: {
        type: 'openai',
        host: 'https://api.mistral.ai',
        envKey: 'MISTRAL_API_KEY',
    },
};

/**
 * 建立 Provider 實例
 *
 * @param {Object} options
 * @param {string} [options.provider] - provider 名稱或別名（自動偵測若未指定）
 * @param {string} [options.host] - 覆蓋預設端點
 * @param {string} [options.apiKey] - API 金鑰（覆蓋環境變數）
 * @param {number} [options.timeout] - 連線逾時（ms）
 * @returns {import('./base-provider').BaseProvider}
 */
function createProvider(options = {}) {
    let providerName = options.provider;

    // 自動偵測：有 API key → openai，否則 → ollama
    if (!providerName) {
        if (options.apiKey || process.env.OPENAI_API_KEY) {
            providerName = 'openai';
        } else {
            providerName = 'ollama';
        }
    }

    providerName = providerName.toLowerCase();

    const alias = PROVIDER_ALIASES[providerName];
    if (!alias) {
        const supported = Object.keys(PROVIDER_ALIASES).join(', ');
        throw new Error(
            `未知的 provider: '${providerName}'\n` +
            `支援的 provider: ${supported}`
        );
    }

    // 解析 API key（優先順序：--api-key > 別名環境變數 > OPENAI_API_KEY）
    const apiKey = options.apiKey
        || (alias.envKey && process.env[alias.envKey])
        || process.env.OPENAI_API_KEY
        || '';

    // 解析 host（--host 覆蓋別名預設值）
    const host = options.host || alias.host;

    if (alias.type === 'ollama') {
        return new OllamaProvider({
            host,
            timeout: options.timeout,
        });
    }

    if (alias.type === 'openai') {
        if (!apiKey && host.startsWith('https://')) {
            // 雲端服務通常需要 API key（本地服務如 vLLM 可能不需要）
            const envHint = alias.envKey ? `\n  或設定環境變數: ${alias.envKey}=<your-key>` : '';
            throw new Error(
                `${providerName} provider 需要 API 金鑰\n` +
                `  使用 --api-key <key> 指定${envHint}`
            );
        }
        return new OpenAIProvider({
            host,
            apiKey,
            apiFormat: alias.apiFormat || 'chat',
            timeout: options.timeout,
        });
    }

    throw new Error(`Provider type '${alias.type}' not implemented`);
}

/** 取得所有支援的 provider 名稱 */
function listProviders() {
    return Object.keys(PROVIDER_ALIASES);
}

module.exports = { createProvider, listProviders, PROVIDER_ALIASES };
