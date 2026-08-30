/**
 * DynamicDetailRenderer
 * 動態明細渲染器 - 從頁面定義 JSON 產生唯讀明細頁
 *
 * Detail definitions may use the legacy flat `fields` shape or the ext detail
 * metadata shape (`detail.tabs`, `detail.subtables`, `detail.attachments`, ...).
 * The renderer keeps the old flat field path intact and renders structured
 * detail metadata when it is present.
 */

import Locale from '../ui_components/i18n/index.js';
import { sanitizeHTML, sanitizeUrl } from '../ui_components/utils/security.js';
import { BasicButton } from '../ui_components/common/BasicButton/BasicButton.js';
import { DataTable } from '../ui_components/layout/DataTable/DataTable.js';
import { DrawerPanel } from '../ui_components/layout/Panel/DrawerPanel.js';

function isObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function asArray(value) {
    if (Array.isArray(value)) return value;
    if (value === undefined || value === null) return [];
    return [value];
}

function isEmptyValue(value) {
    return value === undefined || value === null || value === '';
}

function isDataPath(value) {
    const text = String(value || '');
    return /^[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)*$/.test(text);
}

function hasOwn(target, key) {
    return Object.prototype.hasOwnProperty.call(Object(target), key);
}

export class DynamicDetailRenderer {
    /**
     * @param {Object} options
     * @param {Object} options.definition - 頁面定義 JSON
     * @param {Object} options.data - 資料物件
     * @param {Object} options.routeParams - route params, e.g. { ColValue: "..." }
     * @param {Function} options.onBack - 返回按鈕回調
     * @param {Function} options.onEdit - 編輯按鈕回調
     * @param {Function} options.onAction - 操作回調 (actionId, data, resolvedAction) => void
     * @param {boolean} options.lazyTabs - true 時分頁內容延後到首次啟用才產生（面板元素仍會立即存在）
     */
    constructor(options = {}) {
        this.options = {
            definition: null,
            data: {},
            routeParams: {},
            lookupData: {},
            onBack: null,
            onEdit: null,
            onAction: null,
            strings: {},
            lazyTabs: false,
            sanitizeRichText: sanitizeHTML,
            ...options
        };

        this._element = null;
        this._pendingBuild = true;
        this._buildError = null;
        this._activeTabId = null;
        this._tabButtons = [];
        this._tabPanels = [];
        this._pendingTabPanels = new Map();
        this._controlComponents = [];
    }

    /**
     * 建構延後到第一次讀取 element（或 mount / getActiveTabId），
     * 讓建構後、首次取用前的 init()／setData() 併入同一次建構。
     */
    get element() {
        if (this._pendingBuild) this._ensureBuilt();
        // 建構失敗必須每次讀取都拋：只丟一次的話後續讀取會拿到半成品空殼而靜默渲染空白明細
        if (this._buildError) throw this._buildError;
        return this._element;
    }

    set element(value) {
        this._element = value;
        this._pendingBuild = false;
        this._buildError = null;
    }

    _ensureBuilt() {
        // 旗標必須在 _build() 前關閉：_build() 內部會讀取 this.element，留 true 會讓 getter 無限重入
        this._pendingBuild = false;
        this._runBuild();
    }

    _runBuild() {
        this._buildError = null;
        try {
            this._build();
        } catch (error) {
            this._buildError = error;
            throw error;
        }
    }

    _build() {
        this._destroyControlComponents();
        this._pendingTabPanels.clear();
        const { definition } = this.options;
        this.element = document.createElement('div');
        this.element.className = 'dynamic-detail';

        if (!definition) return;

        const detail = definition.detail;
        if (this._hasStructuredDetail(detail)) {
            this.element.classList.add('dynamic-detail--structured');
            this._buildStructuredDetail();
        } else if (Array.isArray(definition.fields)) {
            this.element.classList.add('dynamic-detail--flat');
            this._buildFlatFields();
        }

        if (this.options.onBack || this.options.onEdit) {
            this.element.appendChild(this._createButtons());
        }
    }

    _hasStructuredDetail(detail) {
        return Boolean(detail && (
            Array.isArray(detail.mainFields) ||
            Array.isArray(detail.tabs) ||
            Array.isArray(detail.steps) ||
            Array.isArray(detail.subtables) ||
            Array.isArray(detail.attachments) ||
            Array.isArray(detail.history) ||
            Array.isArray(detail.media) ||
            Array.isArray(detail.actions)
        ));
    }

    _buildStructuredDetail() {
        const { definition } = this.options;
        const detail = definition.detail || {};
        const page = definition.page || {};

        this.element.dataset.detailPageId = page.id || '';
        this.element.dataset.detailLayout = detail.layout || 'flat';
        if (detail.routeParam) {
            this.element.dataset.routeParamName = detail.routeParam;
            this.element.dataset.routeParamValue = this._stringifyValue(this.options.routeParams?.[detail.routeParam]);
        }

        this.element.appendChild(this._createHeader());
        const steps = this._ordered(detail.steps || []);
        if (steps.length > 0) {
            this.element.appendChild(this._createSteps(steps));
        }

        const tabs = this._ordered(detail.tabs || []);
        if (tabs.length > 0) {
            this.element.appendChild(this._createTabs(tabs));
        } else {
            const body = document.createElement('div');
            body.className = 'dynamic-detail__body';
            this._appendMainContent(body);
            this.element.appendChild(body);
        }
    }

    _buildFlatFields() {
        const { definition, data } = this.options;

        const rowGroups = new Map();
        definition.fields.forEach(def => {
            if (def.fieldType === 'hidden') return;
            const row = def.formRow ?? 0;
            if (!rowGroups.has(row)) rowGroups.set(row, []);
            rowGroups.get(row).push(def);
        });

        const sortedRows = [...rowGroups.keys()].sort((a, b) => a - b);
        sortedRows.forEach(rowNum => {
            const defs = rowGroups.get(rowNum);
            const rowEl = document.createElement('div');
            rowEl.className = 'dynamic-detail__row';
            rowEl.style.cssText = 'display:grid;grid-template-columns:repeat(12, 1fr);gap:16px;margin-bottom:16px;';

            defs.forEach(def => {
                const value = data?.[def.fieldName] ?? data?.[def.name];
                const fieldEl = this._createDetailField(def, value);
                if (def.formCol) {
                    fieldEl.style.gridColumn = `span ${def.formCol}`;
                } else {
                    const colSpan = Math.floor(12 / defs.length);
                    fieldEl.style.gridColumn = `span ${colSpan}`;
                }
                rowEl.appendChild(fieldEl);
            });

            this.element.appendChild(rowEl);
        });
    }

    _createHeader() {
        const { definition } = this.options;
        const detail = definition.detail || {};
        const headerDef = detail.header || {};
        const header = document.createElement('header');
        header.className = 'dynamic-detail__header';
        header.style.cssText = 'display:flex;align-items:flex-start;justify-content:space-between;gap:16px;margin-bottom:18px;';

        const text = document.createElement('div');
        text.className = 'dynamic-detail__header-text';

        const cardTitleValue = headerDef.cardTitle;
        if (cardTitleValue) {
            const eyebrow = document.createElement('div');
            eyebrow.className = 'dynamic-detail__header-card-title';
            eyebrow.style.cssText = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-muted);font-weight:600;margin-bottom:4px;';
            eyebrow.textContent = String(cardTitleValue);
            text.appendChild(eyebrow);
        }

        const title = document.createElement('h2');
        title.className = 'dynamic-detail__title';
        title.style.cssText = 'margin:0;color:var(--cl-text);font-size:var(--cl-font-size-2xl);line-height:1.35;';
        title.textContent = this._resolveDisplayText(headerDef.titleSource || detail.titleSource || headerDef.title || definition.page?.title);
        text.appendChild(title);

        const subtitleText = this._resolveDisplayText(headerDef.subtitleSource);
        if (subtitleText) {
            const subtitle = document.createElement('div');
            subtitle.className = 'dynamic-detail__subtitle';
            subtitle.style.cssText = 'margin-top:4px;color:var(--cl-text-muted);font-size:var(--cl-font-size-md);';
            subtitle.textContent = subtitleText;
            text.appendChild(subtitle);
        }

        header.appendChild(text);

        const actions = this._createActionToolbar();
        if (actions) header.appendChild(actions);

        return header;
    }

    _createSteps(steps) {
        const list = document.createElement('ol');
        list.className = 'dynamic-detail__steps';
        list.setAttribute('data-detail-steps', '');
        list.style.cssText = 'display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px;margin:0 0 18px 0;padding:0;list-style:none;';

        steps.forEach((step, index) => {
            const item = document.createElement('li');
            item.className = 'dynamic-detail__step';
            item.dataset.stepId = step.id || '';
            item.dataset.stepOrder = String(step.order ?? index + 1);
            item.style.cssText = 'border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-md);padding:10px 12px;background:var(--cl-bg-secondary);';

            const title = document.createElement('div');
            title.className = 'dynamic-detail__step-title';
            title.style.cssText = 'font-size:var(--cl-font-size-md);font-weight:700;color:var(--cl-text);';
            title.textContent = step.title || '';
            item.appendChild(title);

            const value = document.createElement('div');
            value.className = 'dynamic-detail__step-value';
            value.style.cssText = 'margin-top:4px;font-size:var(--cl-font-size-sm);color:var(--cl-text-muted);';
            value.textContent = this._resolveDisplayText(step.source) || this._emptyText();
            item.appendChild(value);

            list.appendChild(item);
        });

        return list;
    }

    _createTabs(tabs) {
        const shell = document.createElement('div');
        shell.className = 'dynamic-detail__tabs-shell';

        const tabList = document.createElement('div');
        tabList.className = 'dynamic-detail__tabs';
        tabList.setAttribute('role', 'tablist');
        tabList.setAttribute('data-detail-tabs', '');
        tabList.style.cssText = 'display:flex;gap:4px;overflow:auto;border-bottom:1px solid var(--cl-border);margin-bottom:14px;';

        const panels = document.createElement('div');
        panels.className = 'dynamic-detail__tab-panels';

        this._tabButtons = [];
        this._tabPanels = [];

        const initialTabId = tabs.some(tab => tab.id === this._activeTabId)
            ? this._activeTabId
            : tabs[0]?.id;

        tabs.forEach((tab) => {
            const tabId = tab.id || tab.key || tab.title;
            const component = this._rememberControl(new BasicButton({
                type: BasicButton.TYPES.PLAIN,
                variant: 'plain',
                showIcon: false,
                customLabel: tab.title || tabId,
                onClick: () => this._activateTab(tabId),
            }));
            const button = component.element;
            button.classList.add('dynamic-detail__tab');
            button.textContent = tab.title || tabId;
            button.dataset.tabId = tabId;
            button.dataset.tabKind = tab.kind || '';
            button.setAttribute('role', 'tab');
            button.style.cssText = 'appearance:none;border:0;border-bottom:2px solid transparent;background:transparent;color:var(--cl-text-muted);padding:10px 12px;font-size:var(--cl-font-size-lg);font-weight:600;cursor:pointer;white-space:nowrap;';
            component.mount(tabList);
            this._tabButtons.push(button);

            const panel = document.createElement('section');
            panel.className = 'dynamic-detail__tab-panel';
            panel.dataset.tabPanelId = tabId;
            panel.dataset.tabKind = tab.kind || '';
            panel.setAttribute('role', 'tabpanel');
            if (this.options.lazyTabs) {
                this._pendingTabPanels.set(panel, tab);
            } else {
                this._populateTabPanel(panel, tab);
            }
            panels.appendChild(panel);
            this._tabPanels.push(panel);
        });

        shell.appendChild(tabList);
        shell.appendChild(panels);
        this._activateTab(initialTabId);
        return shell;
    }

    _populateTabPanel(panel, tab) {
        const kind = tab.kind || 'custom';
        if (kind === 'main') {
            this._appendMainContent(panel);
            return;
        }

        if (kind === 'subtable') {
            this._appendSubtables(panel, tab.id);
            this._appendMedia(panel, tab.id);
            return;
        }

        if (kind === 'attachment') {
            this._appendAttachments(panel, tab.id);
            return;
        }

        if (kind === 'history') {
            this._appendHistory(panel, tab.id);
            return;
        }

        if (kind === 'chart') {
            this._appendMedia(panel, tab.id);
            return;
        }

        this._appendMedia(panel, tab.id);
    }

    _appendMainContent(container) {
        const detail = this.options.definition.detail || {};
        const mainFields = (detail.mainFields || []).filter(field => field?.$delete !== true);
        if (mainFields.length > 0) {
            container.appendChild(this._createMainFields(mainFields));
        }

        this._appendSubtables(container, null);
        this._appendMedia(container, null);
    }

    _createMainFields(fields) {
        const grid = document.createElement('div');
        grid.className = 'dynamic-detail__main-fields';
        grid.setAttribute('data-detail-main-fields', '');
        grid.style.cssText = 'display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px;margin-bottom:18px;';

        fields.forEach((field) => grid.appendChild(this._createMainField(field)));

        return grid;
    }

    _createMainField(field) {
        if (field.presentation?.type === 'activity-area-cards') {
            return this._createActivityAreaField(field);
        }

        const item = document.createElement('div');
        item.className = 'dynamic-detail__main-field';
        item.dataset.fieldId = field.id || '';
        item.dataset.fieldSource = this._sourceToString(field.source);
        item.style.cssText = 'border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-md);padding:10px 12px;background:var(--cl-bg);min-width:0;';

        const label = document.createElement('div');
        label.className = 'dynamic-detail__main-field-label';
        label.style.cssText = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-muted);font-weight:600;margin-bottom:4px;';
        label.textContent = field.label || '';
        item.appendChild(label);

        const value = document.createElement('div');
        value.className = 'dynamic-detail__main-field-value';
        value.style.cssText = 'font-size:var(--cl-font-size-lg);color:var(--cl-text);line-height:1.45;overflow-wrap:anywhere;';
        const resolved = this._resolveValueSource(field.source);
        if (Array.isArray(resolved) && Array.isArray(field.options)) {
            const [primary, ...details] = resolved;
            const option = field.options.find(item => String(item?.value ?? '') === String(primary ?? ''));
            const parts = [option?.label || this._stringifyValue(primary), ...details.map(item => this._stringifyValue(item))]
                .filter(Boolean);
            value.textContent = parts.join(' / ') || this._emptyText();
        } else if (field.lookup && !Array.isArray(resolved)) {
            value.textContent = this._lookupDisplayValue(field.lookup, resolved) || this._emptyText();
        } else if (Array.isArray(field.options) && !Array.isArray(resolved)) {
            const option = field.options.find(item => String(item?.value ?? '') === String(resolved ?? ''));
            value.textContent = option?.label || this._stringifyValue(resolved) || this._emptyText();
        } else {
            value.textContent = this._stringifyValue(resolved) || this._emptyText();
        }
        item.appendChild(value);
        return item;
    }

    _createActivityAreaField(field) {
        const presentation = field.presentation || {};
        const primary = presentation.primary || {};
        const secondary = presentation.secondary || {};
        const subtable = (this.options.definition.detail?.subtables || [])
            .find(item => item?.id === secondary.subtableId);
        const rows = subtable ? this._resolveSubtableRows(subtable) : asArray(this._resolveValueSource(secondary.source));

        const item = document.createElement('div');
        item.className = 'dynamic-detail__main-field dynamic-detail__activity-area';
        item.dataset.fieldId = field.id || '';
        item.dataset.fieldSource = this._sourceToString(field.source);
        item.style.cssText = 'grid-column:1 / -1;';

        const label = document.createElement('div');
        label.className = 'dynamic-detail__main-field-label dynamic-detail__activity-area-label';
        label.textContent = presentation.sectionLabel || field.label || '';
        item.appendChild(label);

        const cards = document.createElement('div');
        cards.className = 'dynamic-detail__activity-area-cards';

        const primaryCard = document.createElement('div');
        primaryCard.className = 'dynamic-detail__activity-card dynamic-detail__activity-card--primary';
        primaryCard.appendChild(this._createActivityCardIcon());
        const primaryText = document.createElement('div');
        primaryText.className = 'dynamic-detail__activity-card-text';
        const primaryTitle = document.createElement('strong');
        primaryTitle.textContent = primary.label || '主要地區';
        const primaryValue = document.createElement('span');
        const primaryCode = this._resolveValueSource(primary.source);
        primaryValue.textContent = this._lookupDisplayValue(presentation.lookup, primaryCode) || this._emptyText();
        primaryText.appendChild(primaryTitle);
        primaryText.appendChild(primaryValue);
        primaryCard.appendChild(primaryText);
        cards.appendChild(primaryCard);

        let drawer = null;
        const secondaryCardComponent = this._rememberControl(new BasicButton({
            type: BasicButton.TYPES.CUSTOM,
            variant: 'plain',
            showIcon: false,
            customLabel: secondary.label || '次要活動地區',
            onClick: () => {
                if (!drawer) return;
                drawer.openDrawer?.();
                secondaryCard.setAttribute('aria-expanded', 'true');
            },
        }));
        const secondaryCard = secondaryCardComponent.element;
        secondaryCard.classList.add('dynamic-detail__activity-card', 'dynamic-detail__activity-card--secondary');
        secondaryCard.style.cssText = '';
        secondaryCard.replaceChildren();
        secondaryCard.dataset.detailDrawerTrigger = secondary.subtableId || '';
        secondaryCard.setAttribute('aria-haspopup', 'dialog');
        secondaryCard.setAttribute('aria-expanded', 'false');
        secondaryCard.appendChild(this._createActivityCardIcon());
        const secondaryText = document.createElement('div');
        secondaryText.className = 'dynamic-detail__activity-card-text';
        const secondaryTitle = document.createElement('strong');
        secondaryTitle.textContent = secondary.label || '次要活動地區';
        const secondaryCount = document.createElement('span');
        secondaryCount.textContent = `${rows.length}${secondary.countSuffix || '筆'}`;
        secondaryText.appendChild(secondaryTitle);
        secondaryText.appendChild(secondaryCount);
        secondaryCard.appendChild(secondaryText);
        secondaryCardComponent.mount(cards);
        item.appendChild(cards);

        drawer = this._createActivityAreaDrawer({ field, presentation, subtable, rows, trigger: secondaryCard });
        item.appendChild(drawer);
        secondaryCard.setAttribute('aria-controls', drawer.id);

        return item;
    }

    _createActivityCardIcon() {
        const icon = document.createElement('span');
        icon.className = 'dynamic-detail__activity-card-icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = '';
        return icon;
    }

    _createActivityAreaDrawer({ field, presentation, subtable, rows, trigger }) {
        const title = presentation.drawerTitle || subtable?.title || '次要活動處所';
        const component = this._rememberControl(new DrawerPanel({
            title,
            position: DrawerPanel.POSITIONS.RIGHT,
            width: '420px',
            closable: true,
            autoClose: true,
            onClose: () => trigger.setAttribute('aria-expanded', 'false'),
        }));
        component.backdrop.classList.add('dynamic-detail__drawer-overlay');
        component.backdrop.id = `detail-drawer-${field.id || 'activity-area'}`;
        component.element.classList.add('dynamic-detail__drawer');
        component.element.setAttribute('aria-label', title);

        const list = document.createElement('ul');
        list.className = 'dynamic-detail__drawer-list';
        const valueSource = subtable?.fields?.[0]?.source || presentation.secondary?.valueSource || 'SActAreaID';
        if (rows.length === 0) {
            const empty = document.createElement('li');
            empty.className = 'dynamic-detail__drawer-empty';
            empty.textContent = this._label('emptyTable');
            list.appendChild(empty);
        } else {
            rows.forEach(row => {
                const entry = document.createElement('li');
                entry.className = 'dynamic-detail__drawer-item';
                const pin = document.createElement('span');
                pin.className = 'dynamic-detail__drawer-pin';
                pin.setAttribute('aria-hidden', 'true');
                pin.textContent = '';
                const code = this._resolvePath(row, valueSource);
                const text = document.createElement('span');
                text.textContent = this._lookupDisplayValue(presentation.lookup, code) || this._emptyText();
                entry.appendChild(pin);
                entry.appendChild(text);
                list.appendChild(entry);
            });
        }
        component.setContent(list);
        component.mount(document.body);
        component.backdrop.openDrawer = () => component.open();
        return component.backdrop;
    }

    _lookupDisplayValue(lookup = {}, value) {
        if (isEmptyValue(value)) return '';
        const rows = lookup.items || lookup.options || this.options.lookupData?.[lookup.source] || [];
        const valueField = lookup.valueField || 'value';
        const labelField = lookup.labelField || 'label';
        const match = rows.find(row => String(row?.[valueField] ?? '') === String(value));
        return match ? this._stringifyValue(match[labelField]) : this._stringifyValue(value);
    }

    _appendSubtables(container, tabId) {
        const subtables = this._filterByTab(this.options.definition.detail?.subtables || [], tabId)
            .filter(subtable => subtable.presentation !== 'drawer');
        subtables.forEach((subtable) => {
            container.appendChild(this._createSubtable(subtable));
        });
    }

    _createSubtable(subtable) {
        const section = document.createElement('section');
        section.className = 'dynamic-detail__subtable';
        section.dataset.subtableId = subtable.id || '';
        section.dataset.subtableSource = this._resolveSourceReference(subtable.source);
        section.style.cssText = 'margin-bottom:18px;';

        section.appendChild(this._createSectionTitle(subtable.title || subtable.id || this._label('subtable')));

        const tableWrap = document.createElement('div');
        tableWrap.className = 'dynamic-detail__subtable-wrap';
        tableWrap.style.cssText = 'overflow:auto;border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-md);background:var(--cl-bg);';
        const rows = this._resolveSubtableRows(subtable);
        const fields = subtable.fields || [];
        const data = rows.map(row => Object.fromEntries(fields.map((field, index) => [
            `field${index}`,
            this._stringifyValue(this._resolvePath(row, field.source)) || this._emptyText(),
        ])));
        const table = this._rememberControl(new DataTable(tableWrap, {
            columns: fields.map((field, index) => ({
                key: `field${index}`,
                title: field.label || field.source || '',
            })),
            data,
            pagination: false,
            selectableRows: 'none',
            emptyText: this._label('emptyTable'),
            bordered: true,
        }));
        table.element?.classList.add('dynamic-detail__subtable-table');
        tableWrap.querySelectorAll('thead th').forEach((header, index) => {
            header.classList.add('dynamic-detail__subtable-header');
            header.dataset.fieldSource = fields[index]?.source || '';
        });
        tableWrap.querySelectorAll('tbody tr').forEach(row => row.classList.add('dynamic-detail__subtable-row'));
        tableWrap.querySelectorAll('tbody tr').forEach(row => row.querySelectorAll('td').forEach((cell, index) => {
            cell.classList.add('dynamic-detail__subtable-cell');
            cell.dataset.fieldSource = fields[index]?.source || '';
        }));
        section.appendChild(tableWrap);
        return section;
    }

    _appendAttachments(container, tabId) {
        const attachments = this._filterByTab(this.options.definition.detail?.attachments || [], tabId);
        attachments.forEach((attachment) => {
            container.appendChild(this._createAttachment(attachment));
        });
    }

    _createAttachment(attachment) {
        const section = document.createElement('section');
        section.className = 'dynamic-detail__attachment';
        section.dataset.attachmentId = attachment.id || '';
        section.dataset.component = attachment.component || '';
        section.dataset.tableName = attachment.tableName || '';
        section.dataset.tablePk = this._resolveReference(attachment.tablePk);
        section.dataset.action = attachment.action || '';
        section.style.cssText = 'border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-md);padding:12px;margin-bottom:14px;background:var(--cl-bg);';

        section.appendChild(this._createSectionTitle(attachment.component || this._label('attachments')));
        section.appendChild(this._createMetaList([
            [this._label('tableName'), attachment.tableName],
            [this._label('tablePk'), this._resolveReference(attachment.tablePk)],
            [this._label('action'), attachment.action],
        ]));
        return section;
    }

    _appendHistory(container, tabId) {
        const history = this._filterByTab(this.options.definition.detail?.history || [], tabId);
        history.forEach((item) => {
            container.appendChild(this._createHistory(item));
        });
    }

    _createHistory(item) {
        const section = document.createElement('section');
        section.className = 'dynamic-detail__history';
        section.dataset.historyId = item.id || '';
        section.dataset.component = item.component || '';
        section.dataset.tableName = item.tableName || '';
        section.dataset.tablePk = this._resolveReference(item.tablePk);
        section.style.cssText = 'border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-md);padding:12px;margin-bottom:14px;background:var(--cl-bg);';

        section.appendChild(this._createSectionTitle(item.component || this._label('history')));
        section.appendChild(this._createMetaList([
            [this._label('tableName'), item.tableName],
            [this._label('tablePk'), this._resolveReference(item.tablePk)],
        ]));
        return section;
    }

    _appendMedia(container, tabId) {
        const media = this._filterByTab(this.options.definition.detail?.media || [], tabId);
        media.forEach((item) => {
            container.appendChild(this._createMedia(item));
        });
    }

    _createMedia(item) {
        const section = document.createElement('section');
        section.className = 'dynamic-detail__media';
        section.dataset.mediaId = item.id || '';
        section.dataset.component = item.component || '';
        section.dataset.source = this._resolveSourceReference(item.source);
        section.style.cssText = 'border:1px dashed var(--cl-border);border-radius:var(--cl-radius-md);padding:12px;margin-bottom:14px;background:var(--cl-bg-secondary);';

        section.appendChild(this._createSectionTitle(item.component || this._label('media')));
        section.appendChild(this._createMetaList([
            [this._label('source'), this._resolveSourceReference(item.source)],
        ]));
        return section;
    }

    _createActionToolbar() {
        const actions = (this.options.definition.detail?.actions || []).filter(action => action?.$delete !== true);
        if (actions.length === 0) return null;

        const toolbar = document.createElement('div');
        toolbar.className = 'dynamic-detail__actions';
        toolbar.style.cssText = 'display:flex;align-items:center;gap:8px;flex-wrap:wrap;justify-content:flex-end;';

        actions.forEach((action) => {
            const component = this._rememberControl(new BasicButton({
                type: BasicButton.TYPES.CUSTOM,
                variant: 'plain',
                showIcon: false,
                customLabel: action.label || action.id || this._label('action'),
                onClick: typeof this.options.onAction === 'function' ? () => {
                    const resolvedAction = {
                        ...action,
                        tablePk: this._resolveReference(action.tablePk),
                        route: this._resolveReference(action.route),
                    };
                    this.options.onAction(action.id || '', this.options.data, resolvedAction);
                } : null,
            }));
            const button = component.element;
            button.classList.add('dynamic-detail__action');
            button.textContent = action.label || action.id || this._label('action');
            button.dataset.actionId = action.id || '';
            button.dataset.component = action.component || '';
            button.dataset.tablePk = this._resolveReference(action.tablePk);
            button.dataset.route = this._resolveReference(action.route);
            button.style.cssText = 'padding:7px 12px;border:1px solid var(--cl-border);background:var(--cl-bg);color:var(--cl-text);border-radius:var(--cl-radius-md);cursor:pointer;font-size:var(--cl-font-size-md);';
            component.mount(toolbar);
        });

        return toolbar;
    }

    _createSectionTitle(text) {
        const title = document.createElement('h3');
        title.className = 'dynamic-detail__section-title';
        title.style.cssText = 'margin:0 0 8px 0;color:var(--cl-text);font-size:var(--cl-font-size-lg);line-height:1.35;';
        title.textContent = String(text || '');
        return title;
    }

    _createMetaList(entries) {
        const list = document.createElement('dl');
        list.className = 'dynamic-detail__meta';
        list.style.cssText = 'display:grid;grid-template-columns:max-content minmax(0,1fr);gap:6px 10px;margin:0;font-size:var(--cl-font-size-md);';

        for (const [label, value] of entries) {
            const dt = document.createElement('dt');
            dt.style.cssText = 'color:var(--cl-text-muted);font-weight:600;';
            dt.textContent = label;
            const dd = document.createElement('dd');
            dd.style.cssText = 'margin:0;color:var(--cl-text);overflow-wrap:anywhere;';
            dd.textContent = this._stringifyValue(value) || this._emptyText();
            list.appendChild(dt);
            list.appendChild(dd);
        }

        return list;
    }

    _activateTab(tabId) {
        this._activeTabId = tabId || null;
        for (const button of this._tabButtons) {
            const active = button.dataset.tabId === this._activeTabId;
            button.classList[active ? 'add' : 'remove']('dynamic-detail__tab--active');
            button.setAttribute('aria-selected', active ? 'true' : 'false');
            button.style.borderBottomColor = active ? 'var(--cl-primary)' : 'transparent';
            button.style.color = active ? 'var(--cl-text)' : 'var(--cl-text-muted)';
        }
        for (const panel of this._tabPanels) {
            const active = panel.dataset.tabPanelId === this._activeTabId;
            // 以「是否可見」為唯一條件補內容，並在補完後移出佇列：切回同一分頁不會重建
            if (active) this._populatePendingTabPanel(panel);
            panel.hidden = !active;
            panel.style.display = active ? '' : 'none';
        }
    }

    _populatePendingTabPanel(panel) {
        const tab = this._pendingTabPanels.get(panel);
        if (!tab) return;
        this._pendingTabPanels.delete(panel);
        this._populateTabPanel(panel, tab);
    }

    _ordered(items) {
        return items
            .filter(item => item && item.$delete !== true)
            .map((item, index) => ({ item, index }))
            .sort((a, b) => {
                const orderA = Number.isFinite(Number(a.item.order)) ? Number(a.item.order) : Number.POSITIVE_INFINITY;
                const orderB = Number.isFinite(Number(b.item.order)) ? Number(b.item.order) : Number.POSITIVE_INFINITY;
                if (orderA !== orderB) return orderA - orderB;
                const keyA = Number(a.item.key);
                const keyB = Number(b.item.key);
                if (Number.isFinite(keyA) && Number.isFinite(keyB) && keyA !== keyB) return keyA - keyB;
                return a.index - b.index;
            })
            .map(entry => entry.item);
    }

    _filterByTab(items, tabId) {
        return (items || [])
            .filter(item => item && item.$delete !== true)
            .filter(item => tabId ? item.tabId === tabId : !item.tabId);
    }

    _resolveSubtableRows(subtable) {
        const data = this.options.data || {};
        const source = subtable.source;
        const candidates = [
            data?.subtables?.[subtable.id],
            data?.subtables?.[source],
            data?.detailSubtables?.[subtable.id],
            data?.detailSubtables?.[source],
            data?.[subtable.id],
            data?.[source],
            isDataPath(source) ? this._resolvePath(data, source) : undefined,
        ];

        for (const value of candidates) {
            if (Array.isArray(value)) return value;
        }

        return [];
    }

    _resolveDisplayText(source) {
        const value = this._resolveValueSource(source);
        return this._stringifyValue(value);
    }

    _resolveValueSource(source) {
        if (source === undefined || source === null) return undefined;
        if (Array.isArray(source)) {
            return source
                .map(item => this._resolveValueSource(item))
                .filter(value => !isEmptyValue(value));
        }

        if (typeof source !== 'string') return source;
        if (source.startsWith('$route.')) {
            return this.options.routeParams?.[source.slice('$route.'.length)];
        }
        if (source === '$record') return this.options.data;
        if (source.startsWith('$record.')) {
            return this._resolvePath(this.options.data, source.slice('$record.'.length));
        }
        if (hasOwn(this.options.routeParams, source)) {
            return this.options.routeParams[source];
        }
        if (hasOwn(this.options.data, source)) {
            return this.options.data[source];
        }
        const sourceRoot = this.options.definition?.detail?.sourceRoot;
        const rootValue = sourceRoot ? this._resolvePath(this.options.data, sourceRoot) : null;
        if (isObject(rootValue) && hasOwn(rootValue, source)) {
            return rootValue[source];
        }
        if (isDataPath(source)) {
            const resolved = this._resolvePath(this.options.data, source);
            if (!isEmptyValue(resolved)) return resolved;
        }
        return undefined;
    }

    _resolveReference(source) {
        if (source === undefined || source === null) return '';
        if (Array.isArray(source)) {
            return source.map(item => this._resolveReference(item)).filter(Boolean).join(' / ');
        }
        if (typeof source !== 'string') return this._stringifyValue(source);

        if (source.includes('{')) {
            return source.replace(/\{([^}]+)\}/g, (_, token) => {
                const resolved = this._resolveValueSource(token);
                return encodeURIComponent(this._stringifyValue(resolved) || token);
            });
        }

        const resolved = this._resolveValueSource(source);
        return !isEmptyValue(resolved) ? this._stringifyValue(resolved) : source;
    }

    _resolveSourceReference(source) {
        if (source === undefined || source === null) return '';
        if (Array.isArray(source)) {
            return source.map(item => this._resolveSourceReference(item)).filter(Boolean).join(' / ');
        }
        if (typeof source !== 'string') return this._stringifyValue(source);
        if (!source.includes('{')) return source;
        return source.replace(/\{([^}]+)\}/g, (_, token) => {
            const resolved = this._resolveValueSource(token);
            return encodeURIComponent(this._stringifyValue(resolved) || token);
        });
    }

    _resolvePath(target, path) {
        if (!target || typeof path !== 'string' || path === '') return undefined;
        if (hasOwn(target, path)) return target[path];

        return path.split('.').reduce((current, key) => {
            if (current === undefined || current === null) return undefined;
            return current[key];
        }, target);
    }

    _sourceToString(source) {
        if (Array.isArray(source)) return source.map(item => this._sourceToString(item)).join('|');
        return source === undefined || source === null ? '' : String(source);
    }

    _stringifyValue(value) {
        if (isEmptyValue(value)) return '';
        if (Array.isArray(value)) {
            return value
                .map(item => this._stringifyValue(item))
                .filter(Boolean)
                .join(' / ');
        }
        if (isObject(value)) {
            return Object.values(value)
                .map(item => this._stringifyValue(item))
                .filter(Boolean)
                .join(' / ');
        }
        return String(value);
    }

    _label(key) {
        const overrides = this.options.strings || {};
        if (overrides[key]) return overrides[key];
        return Locale.t(`dynamicDetail.${key}`);
    }

    _emptyText() {
        return this._label('emptyValue');
    }

    _createDetailField(def, value) {
        const container = document.createElement('div');
        container.className = 'dynamic-detail__field';

        const label = document.createElement('div');
        label.className = 'dynamic-detail__label';
        label.textContent = def.label;
        label.style.cssText = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-muted);margin-bottom:4px;font-weight:500;';

        const valueEl = document.createElement('div');
        valueEl.className = 'dynamic-detail__value';
        valueEl.style.cssText = 'font-size:var(--cl-font-size-lg);color:var(--cl-text);min-height:20px;';

        const formatted = this._formatValue(def, value);
        const isElement = formatted
            && typeof formatted === 'object'
            && (formatted.nodeType || (typeof HTMLElement !== 'undefined' && formatted instanceof HTMLElement));
        if (isElement) {
            valueEl.appendChild(formatted);
        } else {
            valueEl.textContent = formatted == null ? '' : String(formatted);
        }

        container.appendChild(label);
        container.appendChild(valueEl);
        return container;
    }

    /**
     * 依 fieldType 格式化顯示值
     *
     * CSP-safe：需要樣式的結果回傳 DOM Node（createElement + style.cssText），
     * 純文字結果回傳字串並由呼叫端以 textContent 寫入。
     * @returns {string|Node}
     */
    _formatValue(def, value) {
        if (value === null || value === undefined || value === '') {
            return this._emptyPlaceholder();
        }

        switch (def.fieldType) {
            case 'date':
                return this._formatDate(value);

            case 'time':
                return String(value);

            case 'checkbox':
            case 'toggle':
                return this._formatBoolean(value);

            case 'select':
            case 'radio':
                return this._formatOption(def, value);

            case 'multiselect':
                return this._formatMultiOption(def, value);

            case 'color': {
                const wrap = document.createElement('span');
                wrap.style.cssText = 'display:inline-flex;align-items:center;gap:6px;';
                const swatch = document.createElement('span');
                swatch.style.cssText = 'display:inline-block;width:16px;height:16px;border-radius:var(--cl-radius-xs);border:1px solid var(--cl-border);';
                const color = String(value).trim();
                swatch.style.background = /^#[0-9a-f]{3,8}$/i.test(color) ? color : 'transparent';
                wrap.appendChild(swatch);
                const text = document.createElement('span');
                text.textContent = color;
                wrap.appendChild(text);
                return wrap;
            }

            case 'image': {
                const source = sanitizeUrl(String(value));
                if (!source || source.startsWith('//') || /^(?:mailto|tel):/i.test(source)) return this._emptyPlaceholder();
                const img = document.createElement('img');
                img.src = source;
                img.style.cssText = 'max-width:120px;max-height:80px;border-radius:var(--cl-radius-sm);border:1px solid var(--cl-border-light);';
                return img;
            }

            case 'password':
                return '••••••••';

            case 'datetime':
                return this._formatDateTime(value);

            case 'richtext': {
                const box = document.createElement('div');
                box.style.cssText = 'max-height:80px;overflow:hidden;border:1px solid var(--cl-border-light);padding:4px 8px;border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-md);';
                box.innerHTML = this.options.sanitizeRichText(String(value));
                return box;
            }

            case 'canvas': {
                const hint = document.createElement('div');
                hint.style.cssText = 'color:var(--cl-text-muted);font-size:var(--cl-font-size-sm);';
                hint.textContent = '（繪圖內容）';
                return hint;
            }

            case 'geolocation':
                if (typeof value === 'object') {
                    return String(value.address?.shortName || `${value.lat}, ${value.lng}`);
                }
                return String(value);

            case 'weather':
                if (typeof value === 'object') {
                    return `${value.icon || ''} ${value.temperature || ''}${value.unit || ''} ${value.description || ''}`.trim();
                }
                return String(value);

            case 'address':
                if (typeof value === 'object') {
                    return [value.city, value.district, value.address].filter(Boolean).join('');
                }
                return String(value);

            case 'addresslist':
            case 'phonelist':
            case 'socialmedia':
            case 'personinfo':
                return this._formatListValue(value);

            case 'organization':
                if (typeof value === 'object') {
                    return [value.level1, value.level2, value.level3, value.level4].filter(Boolean).join(' / ');
                }
                return String(value);

            case 'student':
                if (typeof value === 'object') {
                    return value.isStudent ? `學生 - ${value.schoolName || ''}` : '非學生';
                }
                return String(value);

            case 'chained':
            case 'list':
                return this._formatListValue(value);

            default:
                return String(value);
        }
    }

    _formatDate(value) {
        try {
            const d = value instanceof Date ? value : new Date(value);
            if (isNaN(d.getTime())) return String(value);
            return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')}`;
        } catch {
            return String(value);
        }
    }

    _formatBoolean(value) {
        const isTrue = value === true || value === 'true' || value === 1;
        const bgColor = isTrue ? 'var(--cl-success-light)' : 'var(--cl-bg-secondary)';
        const fgColor = isTrue ? 'var(--cl-success)' : 'var(--cl-grey)';
        const badge = document.createElement('span');
        badge.style.cssText = `display:inline-block;padding:2px 8px;border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-sm);background:${bgColor};color:${fgColor};font-weight:500;`;
        badge.textContent = isTrue ? '是' : '否';
        return badge;
    }

    _formatOption(def, value) {
        if (def.optionsSource?.type === 'static') {
            const item = def.optionsSource.items.find(i => i.value === value);
            if (item) return String(item.label);
        }
        return String(value);
    }

    _formatMultiOption(def, value) {
        const values = Array.isArray(value) ? value : [];
        if (values.length === 0) return this._emptyPlaceholder();

        const items = def.optionsSource?.type === 'static' ? def.optionsSource.items : null;
        const wrap = document.createElement('span');
        values.forEach(v => {
            const item = items ? items.find(i => i.value === v) : null;
            const tag = document.createElement('span');
            tag.style.cssText = 'display:inline-block;padding:2px 8px;margin:2px;border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-sm);background:var(--cl-bg-active);color:var(--cl-primary-dark);';
            tag.textContent = String(item ? item.label : v);
            wrap.appendChild(tag);
        });
        return wrap;
    }

    _formatDateTime(value) {
        try {
            const d = value instanceof Date ? value : new Date(value);
            if (isNaN(d.getTime())) return String(value);
            return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
        } catch {
            return String(value);
        }
    }

    _formatListValue(value) {
        if (Array.isArray(value)) {
            if (value.length === 0) return this._emptyPlaceholder();
            const wrap = document.createElement('div');
            value.forEach((item, i) => {
                const text = typeof item === 'object' ? Object.values(item).filter(Boolean).join(' / ') : String(item);
                const line = document.createElement('div');
                line.style.cssText = 'padding:2px 0;font-size:var(--cl-font-size-md);';
                line.textContent = `${i + 1}. ${text}`;
                wrap.appendChild(line);
            });
            return wrap;
        }
        if (typeof value === 'object') {
            return Object.values(value).filter(Boolean).join(' / ');
        }
        return String(value);
    }

    /**
     * 空值佔位符（CSP-safe：以 CSSOM 指派樣式）
     */
    _emptyPlaceholder() {
        const span = document.createElement('span');
        span.style.cssText = 'color:var(--cl-text-light);';
        span.textContent = this._emptyText();
        return span;
    }

    _createButtons() {
        const footer = document.createElement('div');
        footer.className = 'dynamic-detail__footer';
        footer.style.cssText = 'display:flex;justify-content:flex-end;gap:8px;margin-top:24px;padding-top:16px;border-top:1px solid var(--cl-border-light);';

        if (this.options.onBack) {
            const back = this._rememberControl(new BasicButton({
                type: BasicButton.TYPES.BACK,
                variant: 'plain',
                showIcon: false,
                customLabel: Locale.t('basicButton.back'),
                onClick: () => this.options.onBack(),
            }));
            const backBtn = back.element;
            backBtn.textContent = Locale.t('basicButton.back');
            backBtn.style.cssText = 'padding:8px 20px;border:1px solid var(--cl-border);background:var(--cl-bg);color:var(--cl-text);border-radius:var(--cl-radius-md);cursor:pointer;font-size:var(--cl-font-size-lg);';
            back.mount(footer);
        }

        if (this.options.onEdit) {
            const edit = this._rememberControl(new BasicButton({
                type: BasicButton.TYPES.CUSTOM,
                variant: 'primary',
                showIcon: false,
                customLabel: Locale.t('actionButton.edit'),
                onClick: () => this.options.onEdit(),
            }));
            const editBtn = edit.element;
            editBtn.textContent = Locale.t('actionButton.edit');
            editBtn.style.cssText = 'padding:8px 20px;border:1px solid var(--cl-primary);background:var(--cl-primary);color:var(--cl-primary-contrast);border-radius:var(--cl-radius-md);cursor:pointer;font-size:var(--cl-font-size-lg);';
            edit.mount(footer);
        }

        return footer;
    }

    setData(data) {
        this.options.data = data || {};
        if (this._pendingBuild) return;
        const parent = this._element?.parentNode;
        if (parent) {
            this._element.remove();
            this._runBuild();
            parent.appendChild(this._element);
        } else {
            this._runBuild();
        }
    }

    setRouteParams(routeParams = {}) {
        this.options.routeParams = routeParams;
        this.setData(this.options.data);
    }

    getActiveTabId() {
        if (this._pendingBuild) this._ensureBuilt();
        return this._activeTabId;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target && this.element) target.appendChild(this.element);
        return this;
    }

    _rememberControl(component) {
        this._controlComponents.push(component);
        return component;
    }

    _destroyControlComponents() {
        this._controlComponents?.splice(0).forEach(component => component.destroy?.());
    }

    destroy() {
        this._pendingBuild = false;
        this._pendingTabPanels.clear();
        this._destroyControlComponents();
        if (this._element?.parentNode) {
            this._element.remove();
        }
    }
}

export default DynamicDetailRenderer;
