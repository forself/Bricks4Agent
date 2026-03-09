/**
 * VizEngine.js
 * 
 * 統一視覺化引擎標準介面 (Unified Visualization Engine Standard Interface)
 * "JSON Array Protocol" Edition with Security Enhancements
 * 
 * 核心概念 (Core Concept):
 * 1. 數據輸入規範為「JSON 物件陣列」(Array of JSON Objects)。
 *    Data inputs are standardized as "Array of JSON Objects".
 * 2. 支援安全數據抓取 (Secure Data Fetching) 與 JWT 整合。
 *    Supports secure data fetching with JWT integration.
 */

export class VizEngine {
    constructor() {
        this.instances = new Map();
        this.instanceCounter = 0;
        // Security: Token should be kept private and not logged
        this._authToken = null;
    }

    /**
     * 設定權杖 (Set Authentication Token)
     * @param {string} token - JWT Token
     */
    setAuth(token) {
        this._authToken = token;
    }

    /**
     * 安全抓取資料 (Secure Fetch)
     * 自動掛載 Authorization Header
     * 
     * @param {string} url - API Endpoint
     * @param {Object} [options] - Fetch Options
     * @returns {Promise<any>} JSON Response
     */
    async fetch(url, options = {}) {
        const headers = {
            'Content-Type': 'application/json',
            ...options.headers
        };

        if (this._authToken) {
            headers['Authorization'] = `Bearer ${this._authToken}`;
        }

        try {
            const response = await globalThis.fetch(url, {
                ...options,
                headers
            });

            if (!response.ok) {
                // Security: Do not log the full response or headers in production
                throw new Error(`API Error: ${response.status}`);
            }

            return await response.json();
        } catch (err) {
            console.error('Fetch failed:', err.message); // Minimal logging
            throw err;
        }
    }

    /**
     * 通用渲染入口 (Generic Render)
     * 
     * @param {Object} config
     * @param {string} config.type - 圖表類型 (relation, org, hierarchy, timeline, sankey, flame, sunburst)
     * @param {HTMLElement|string} config.container - 容器
     * @param {Array<Object>} config.data - JSON 物件陣列 (JSON Array)
     * @param {Object} [config.mapping] - 欄位別名 (ex: { id: 'emp_id', label: 'emp_name' })
     * @param {Object} [config.events] - 事件監聽器 (ex: { click: (node) => {} })
     * @param {Object} [config.options] - 進階選項 (colors, layout, etc.)
     * @returns {Object} 圖表實例 ID (Chart Instance ID)
     */
    render(config) {
        console.log('VizEngine.render called with JSON Array:', config);

        // 1. Data Transformation (Alias -> Standard Keys)
        // 引擎負責將不同欄位名稱的資料轉換為圖表標準格式 (id, label, value...)
        const transformedData = this._transformData(config.type, config.data, config.mapping);

        // 2. Instantiate Chart
        // TODO: Switch config.type -> new Chart(transformedData)
        // For now, this is a stub as requested.

        return { instanceId: ++this.instanceCounter };
    }

    /**
     * 綁定事件 (Bind Event)
     */
    on(instanceId, eventName, handler) {
        // TODO: Find instance and attach event listener
    }

    // --- Data Transformation Helpers ---

    _transformData(type, rows, mapping) {
        if (!rows || !Array.isArray(rows) || rows.length === 0) return null;

        // 1. Standardize Fields (Apply Mapping/Aliasing)
        // If mapping provided, transform keys
        const standardizedRows = mapping ? this._applyMapping(rows, mapping) : rows;

        switch (type) {
            case 'relation':
            case 'sankey':
                // List -> Nodes/Links Graph
                return this._rowsToGraph(standardizedRows);
            case 'org':
            case 'hierarchy':
            case 'flame':
            case 'sunburst':
                // List -> Tree (Adjacency List)
                return this._rowsToTree(standardizedRows);
            case 'timeline':
                // List -> Event Objects (Already Objects, just return)
                return standardizedRows;
            default:
                return standardizedRows;
        }
    }

    _applyMapping(rows, mapping) {
        // mapping: { standardKey: 'sourceKey' }
        // ex: { id: 'emp_id', label: 'u_name' }
        return rows.map(row => {
            const newRow = { ...row }; // Keep original data
            for (const [standardKey, sourceKey] of Object.entries(mapping)) {
                if (row[sourceKey] !== undefined) {
                    newRow[standardKey] = row[sourceKey];
                }
            }
            return newRow;
        });
    }

    _rowsToTree(rows) {
        // List to Tree via id/parentId
        const itemMap = {};

        // 1. Build Map
        rows.forEach(item => {
            const id = item.id || item.key || item.name;
            if (id) itemMap[id] = { ...item, children: [] };
        });

        let root = null;

        // 2. Link Parent-Child
        Object.values(itemMap).forEach(item => {
            const pId = item.parentId || item.parent_id || item.parent;
            if (pId && itemMap[pId]) {
                itemMap[pId].children.push(item);
            } else {
                if (!root) root = item; // First root candidate
            }
        });

        return root || rows;
    }

    _rowsToGraph(rows) {
        // List to Graph (Nodes/Links)
        // Assume Rows = Links (Source -> Target)
        const links = rows.filter(r => r.source && r.target);

        const nodeIds = new Set();
        links.forEach(l => {
            nodeIds.add(l.source);
            nodeIds.add(l.target);
        });

        const nodes = Array.from(nodeIds).map(id => ({ id, label: id }));

        return { nodes, links };
    }

    /**
     * 銷毀圖表實例 (Destroy Chart Instance)
     */
    destroy(instanceId) {
        // TODO: Locate instance and call its destroy method
    }
}
