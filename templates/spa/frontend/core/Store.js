/**
 * SPA Store - 狀態管理系統
 *
 * 功能：
 * - 響應式狀態管理
 * - 訂閱/取消訂閱機制
 * - 持久化支援
 * - 狀態快照與恢復
 * - 模組化狀態
 *
 * @module Store
 * @version 1.0.0
 *
 * @example
 * const store = new Store({
 *     user: null,
 *     theme: 'light',
 *     count: 0
 * });
 *
 * // 訂閱變化
 * store.subscribe('count', (value) => console.log('Count:', value));
 *
 * // 更新狀態
 * store.set('count', 1);
 * store.update('count', (c) => c + 1);
 */

export class Store {
    /**
     * @param {Object} initialState - 初始狀態
     * @param {Object} options - 配置選項
     * @param {boolean} options.persist - 是否持久化到 localStorage
     * @param {string} options.persistKey - 持久化的鍵名
     */
    constructor(initialState = {}, options = {}) {
        this._state = { ...initialState };
        this._subscribers = new Map();
        this._globalSubscribers = [];
        this._modules = new Map();

        this._persist = options.persist || false;
        this._persistKey = options.persistKey || 'spa_store';
        this._persistFields = options.persistFields || null;

        // 從 localStorage 恢復狀態
        if (this._persist) {
            this._loadPersistedState();
        }
    }

    /**
     * 取得狀態值
     * @param {string} key - 狀態鍵
     * @returns {any} 狀態值
     */
    get(key) {
        if (key.includes('.')) {
            return this._getNestedValue(key);
        }
        return this._state[key];
    }

    /**
     * 設定狀態值
     * @param {string} key - 狀態鍵
     * @param {any} value - 新值
     */
    set(key, value) {
        const oldValue = this.get(key);

        if (key.includes('.')) {
            this._setNestedValue(key, value);
        } else {
            this._state[key] = value;
        }

        // 通知訂閱者
        this._notifySubscribers(key, value, oldValue);

        // 持久化
        if (this._persist) {
            this._savePersistedState();
        }
    }

    /**
     * 更新狀態值 (使用函數)
     * @param {string} key - 狀態鍵
     * @param {Function} updater - 更新函數 (currentValue) => newValue
     */
    update(key, updater) {
        const currentValue = this.get(key);
        const newValue = updater(currentValue);
        this.set(key, newValue);
    }

    /**
     * 批次更新多個狀態
     * @param {Object} updates - 更新物件 { key: value }
     */
    setMany(updates) {
        const oldValues = {};

        Object.entries(updates).forEach(([key, value]) => {
            oldValues[key] = this.get(key);

            if (key.includes('.')) {
                this._setNestedValue(key, value);
            } else {
                this._state[key] = value;
            }
        });

        // 通知訂閱者
        Object.entries(updates).forEach(([key, value]) => {
            this._notifySubscribers(key, value, oldValues[key]);
        });

        // 持久化
        if (this._persist) {
            this._savePersistedState();
        }
    }

    /**
     * 訂閱特定狀態變化
     * @param {string} key - 狀態鍵
     * @param {Function} callback - 回調函數 (newValue, oldValue) => void
     * @returns {Function} 取消訂閱函數
     */
    subscribe(key, callback) {
        if (!this._subscribers.has(key)) {
            this._subscribers.set(key, []);
        }

        this._subscribers.get(key).push(callback);

        // 返回取消訂閱函數
        return () => {
            const subs = this._subscribers.get(key);
            const index = subs.indexOf(callback);
            if (index > -1) {
                subs.splice(index, 1);
            }
        };
    }

    /**
     * 訂閱所有狀態變化
     * @param {Function} callback - 回調函數 (key, newValue, oldValue) => void
     * @returns {Function} 取消訂閱函數
     */
    subscribeAll(callback) {
        this._globalSubscribers.push(callback);

        return () => {
            const index = this._globalSubscribers.indexOf(callback);
            if (index > -1) {
                this._globalSubscribers.splice(index, 1);
            }
        };
    }

    /**
     * 取得完整狀態快照
     * @returns {Object} 狀態快照
     */
    getSnapshot() {
        return JSON.parse(JSON.stringify(this._state));
    }

    /**
     * 從快照恢復狀態
     * @param {Object} snapshot - 狀態快照
     */
    restoreSnapshot(snapshot) {
        const oldState = this.getSnapshot();
        this._state = JSON.parse(JSON.stringify(snapshot));

        // 通知所有變化
        Object.keys(this._state).forEach(key => {
            if (this._state[key] !== oldState[key]) {
                this._notifySubscribers(key, this._state[key], oldState[key]);
            }
        });

        if (this._persist) {
            this._savePersistedState();
        }
    }

    /**
     * 重置狀態到初始值
     * @param {string} key - 可選，指定要重置的鍵
     */
    reset(key) {
        if (key) {
            this.set(key, undefined);
        } else {
            const oldState = this.getSnapshot();
            this._state = {};

            Object.keys(oldState).forEach(k => {
                this._notifySubscribers(k, undefined, oldState[k]);
            });

            if (this._persist) {
                localStorage.removeItem(this._persistKey);
            }
        }
    }

    /**
     * 註冊狀態模組
     * @param {string} name - 模組名稱
     * @param {Object} moduleState - 模組初始狀態
     */
    registerModule(name, moduleState) {
        this._modules.set(name, moduleState);
        this._state[name] = { ...moduleState };
    }

    /**
     * 取消註冊模組
     * @param {string} name - 模組名稱
     */
    unregisterModule(name) {
        this._modules.delete(name);
        delete this._state[name];
    }

    /**
     * 取得巢狀值
     */
    _getNestedValue(path) {
        const keys = path.split('.');
        let value = this._state;

        for (const key of keys) {
            if (value === undefined || value === null) return undefined;
            value = value[key];
        }

        return value;
    }

    /**
     * 設定巢狀值
     */
    _setNestedValue(path, value) {
        const keys = path.split('.');
        const lastKey = keys.pop();
        let target = this._state;

        for (const key of keys) {
            if (target[key] === undefined) {
                target[key] = {};
            }
            target = target[key];
        }

        target[lastKey] = value;
    }

    /**
     * 通知訂閱者
     */
    _notifySubscribers(key, newValue, oldValue) {
        // 通知特定 key 的訂閱者
        const subscribers = this._subscribers.get(key);
        if (subscribers) {
            subscribers.forEach(callback => {
                try {
                    callback(newValue, oldValue);
                } catch (error) {
                    console.error(`[Store] 訂閱者執行錯誤 (${key}):`, error);
                }
            });
        }

        // 通知全域訂閱者
        this._globalSubscribers.forEach(callback => {
            try {
                callback(key, newValue, oldValue);
            } catch (error) {
                console.error('[Store] 全域訂閱者執行錯誤:', error);
            }
        });
    }

    /**
     * 載入持久化狀態
     */
    _loadPersistedState() {
        try {
            const saved = localStorage.getItem(this._persistKey);
            if (saved) {
                const parsed = JSON.parse(saved);

                // 如果指定了要持久化的欄位，只恢復這些欄位
                if (this._persistFields) {
                    this._persistFields.forEach(field => {
                        if (parsed[field] !== undefined) {
                            this._state[field] = parsed[field];
                        }
                    });
                } else {
                    Object.assign(this._state, parsed);
                }
            }
        } catch (error) {
            console.warn('[Store] 載入持久化狀態失敗:', error);
        }
    }

    /**
     * 保存持久化狀態
     */
    _savePersistedState() {
        try {
            let toSave = this._state;

            // 如果指定了要持久化的欄位，只保存這些欄位
            if (this._persistFields) {
                toSave = {};
                this._persistFields.forEach(field => {
                    if (this._state[field] !== undefined) {
                        toSave[field] = this._state[field];
                    }
                });
            }

            localStorage.setItem(this._persistKey, JSON.stringify(toSave));
        } catch (error) {
            console.warn('[Store] 保存持久化狀態失敗:', error);
        }
    }
}

export default Store;
