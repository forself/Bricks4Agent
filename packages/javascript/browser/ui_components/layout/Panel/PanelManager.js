/**
 * PanelManager
 * 容器管理器 - 管理所有 Panel 的狀態、層級、嵌套關係
 */

class PanelManagerClass {
    constructor() {
        this.panels = new Map();       // panelId -> panel instance
        this.children = new Map();     // parentId -> Set<childId>
        this.parents = new Map();      // childId -> parentId
        this.zIndexBase = 1000;
        this.zIndexStep = 10;
        this.modalStack = [];          // 模態面板堆疊
        this.focusStack = [];          // 聚焦面板堆疊
        this.previousStates = new Map(); // 儲存禁用前的狀態
    }

    /**
     * 註冊 Panel
     */
    register(panel, parent = null) {
        const id = panel.id;
        this.panels.set(id, panel);

        if (parent) {
            const parentId = parent.id;
            this.parents.set(id, parentId);

            if (!this.children.has(parentId)) {
                this.children.set(parentId, new Set());
            }
            this.children.get(parentId).add(id);
        }

        return this;
    }

    /**
     * 取消註冊 Panel
     */
    unregister(panel) {
        const id = panel.id;
        const parentId = this.parents.get(id);

        // 從父容器的子集合中移除
        if (parentId && this.children.has(parentId)) {
            this.children.get(parentId).delete(id);
        }

        // 移除子容器的父參考
        if (this.children.has(id)) {
            this.children.get(id).forEach(childId => {
                this.parents.delete(childId);
            });
            this.children.delete(id);
        }

        this.parents.delete(id);
        this.panels.delete(id);
        this.previousStates.delete(id);

        return this;
    }

    /**
     * 取得 Panel
     */
    get(id) {
        return this.panels.get(id);
    }

    /**
     * 取得父容器
     */
    getParent(panel) {
        const parentId = this.parents.get(panel.id);
        return parentId ? this.panels.get(parentId) : null;
    }

    /**
     * 取得所有子容器
     */
    getChildren(panel) {
        const childIds = this.children.get(panel.id);
        if (!childIds) return [];
        return Array.from(childIds).map(id => this.panels.get(id)).filter(Boolean);
    }

    /**
     * 取得同層容器
     */
    getSiblings(panel) {
        const parentId = this.parents.get(panel.id);
        if (!parentId) {
            // 根層級的所有 panel（無父容器者）
            return Array.from(this.panels.values()).filter(p =>
                !this.parents.has(p.id) && p.id !== panel.id
            );
        }

        const siblingIds = this.children.get(parentId);
        if (!siblingIds) return [];
        return Array.from(siblingIds)
            .filter(id => id !== panel.id)
            .map(id => this.panels.get(id))
            .filter(Boolean);
    }

    /**
     * 取得所有後代容器
     */
    getDescendants(panel) {
        const descendants = [];
        const traverse = (p) => {
            const children = this.getChildren(p);
            children.forEach(child => {
                descendants.push(child);
                traverse(child);
            });
        };
        traverse(panel);
        return descendants;
    }

    /**
     * 計算 z-index
     */
    calculateZIndex(panel) {
        const depth = this._getDepth(panel);
        const modalIndex = this.modalStack.indexOf(panel.id);
        const extraZ = modalIndex >= 0 ? (modalIndex + 1) * 100 : 0;
        return this.zIndexBase + (depth * this.zIndexStep) + extraZ;
    }

    _getDepth(panel) {
        let depth = 0;
        let current = panel;
        while (this.parents.has(current.id)) {
            depth++;
            current = this.panels.get(this.parents.get(current.id));
        }
        return depth;
    }

    /**
     * 進入 Modal 模式
     */
    enterModal(panel) {
        this.modalStack.push(panel.id);
        this._disableOthers(panel, panel.options.modalScope);
    }

    /**
     * 離開 Modal 模式
     */
    exitModal(panel) {
        const index = this.modalStack.indexOf(panel.id);
        if (index >= 0) {
            this.modalStack.splice(index, 1);
        }
        this._restoreOthers(panel);
    }

    /**
     * 進入 Focus 模式
     */
    enterFocus(panel) {
        this.focusStack.push(panel.id);
        this._disableOthers(panel, panel.options.focusScope);
    }

    /**
     * 離開 Focus 模式
     */
    exitFocus(panel) {
        const index = this.focusStack.indexOf(panel.id);
        if (index >= 0) {
            this.focusStack.splice(index, 1);
        }
        this._restoreOthers(panel);
    }

    /**
     * 禁用其他容器
     */
    _disableOthers(panel, scope = 'siblings') {
        let targets = [];

        if (scope === 'siblings') {
            targets = this.getSiblings(panel);
        } else if (scope === 'all') {
            targets = Array.from(this.panels.values()).filter(p => p.id !== panel.id);
        } else if (scope === 'parent') {
            // 只在父容器範圍內
            const parent = this.getParent(panel);
            if (parent) {
                targets = this.getChildren(parent).filter(p => p.id !== panel.id);
            }
        }

        targets.forEach(p => {
            // 儲存原始狀態
            if (!this.previousStates.has(p.id)) {
                this.previousStates.set(p.id, {
                    disabled: p.options.disabled,
                    disabledBy: panel.id
                });
            }
            p.setDisabled(true);
        });
    }

    /**
     * 恢復其他容器
     */
    _restoreOthers(panel) {
        this.previousStates.forEach((state, id) => {
            if (state.disabledBy === panel.id) {
                const p = this.panels.get(id);
                if (p) {
                    p.setDisabled(state.disabled);
                }
                this.previousStates.delete(id);
            }
        });
    }

    /**
     * 全部重置
     */
    reset() {
        this.modalStack = [];
        this.focusStack = [];
        this.previousStates.clear();
        this.panels.forEach(p => p.setDisabled(false));
    }
}

// 單例
export const PanelManager = new PanelManagerClass();
export default PanelManager;
