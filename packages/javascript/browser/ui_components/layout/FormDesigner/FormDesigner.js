import { BasicButton } from '../../common/BasicButton/index.js';
import { Icon } from '../../common/Icon/index.js';
import { Checkbox } from '../../form/Checkbox/index.js';
import { DatePicker } from '../../form/DatePicker/index.js';
import { Dropdown } from '../../form/Dropdown/index.js';
import { NumberInput } from '../../form/NumberInput/index.js';
import { TextArea } from '../../form/TextArea/index.js';
import { TextInput } from '../../form/TextInput/index.js';
import { ToggleSwitch } from '../../form/ToggleSwitch/index.js';
import {
    FORM_DESIGNER_COMPONENTS,
    FORM_DESIGNER_COMPONENT_FIELD_TYPES,
    FORM_DESIGNER_COMPONENT_ICONS,
    cloneFormDesignerJson,
    cloneFormDesignerFields,
    normalizeFormDesignerFields,
    packFormDesignerFields,
    tryMoveFormDesignerField,
    tryResizeFormDesignerField,
} from './layout-helpers.js';

const STYLE_MARKER = 'data-bricks-form-designer-style';

function ensureStyles(documentRef) {
    if (!documentRef?.head || documentRef.head.querySelector(`link[${STYLE_MARKER}]`)) return;
    const link = documentRef.createElement('link');
    link.rel = 'stylesheet';
    link.href = new URL('./FormDesigner.css', import.meta.url).href;
    link.setAttribute(STYLE_MARKER, '');
    documentRef.head.appendChild(link);
}

function definitionFields(value) {
    if (Array.isArray(value)) return value;
    return Array.isArray(value?.fields) ? value.fields : [];
}

function definitionBase(value) {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
    const cloned = cloneFormDesignerJson(value);
    delete cloned.fields;
    return cloned;
}

function selectedField(fields, fieldId) {
    return fields.find((field) => field.field_id === fieldId) || null;
}

function componentItems() {
    return FORM_DESIGNER_COMPONENTS.map((value) => ({ value, label: value }));
}

export class FormDesigner {
    constructor(options = {}) {
        this.options = {
            definition: null,
            fields: null,
            value: null,
            columns: 12,
            onChange: null,
            onSelect: null,
            ...options,
        };
        this.columns = Math.min(24, Math.max(1, Number(this.options.columns) || 12));
        const initialValue = this.options.fields ?? this.options.definition ?? this.options.value ?? [];
        this._definitionBase = this.options.fields !== null
            ? {}
            : definitionBase(initialValue);
        this._fields = normalizeFormDesignerFields(definitionFields(initialValue), this.columns);
        this._selectedId = this._fields[0]?.field_id || null;
        this._container = null;
        this._document = null;
        this._children = [];
        this._listeners = [];
        this._tileById = new Map();
        this._rowById = new Map();
        this._dragFieldId = null;
        this._pointerCleanup = null;
        this.element = null;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target || typeof target.appendChild !== 'function') {
            throw new TypeError('FormDesigner.mount(container) requires a DOM container.');
        }
        if (this.element) {
            if (target !== this._container) throw new Error('FormDesigner is already mounted in another container.');
            return this;
        }
        this._container = target;
        this._document = target.ownerDocument || document;
        ensureStyles(this._document);
        this.element = this._document.createElement('div');
        this.element.className = 'form-designer';
        target.appendChild(this.element);
        this._render();
        return this;
    }

    _listen(element, type, handler, options) {
        element.addEventListener(type, handler, options);
        this._listeners.push({ element, type, handler, options });
    }

    _clearRendered() {
        this._pointerCleanup?.();
        this._pointerCleanup = null;
        for (const { element, type, handler, options } of this._listeners) {
            element.removeEventListener(type, handler, options);
        }
        this._listeners = [];
        this._children.splice(0).reverse().forEach((child) => child?.destroy?.());
        this._tileById.clear();
        this._rowById.clear();
        this.element?.replaceChildren();
    }

    _track(component) {
        this._children.push(component);
        return component;
    }

    _render() {
        if (!this.element) return;
        this._clearRendered();
        const schema = this._document.createElement('section');
        schema.className = 'form-designer__schema';
        schema.setAttribute('aria-label', '資料表欄位');
        schema.appendChild(this._createPanelHeader('資料表欄位', true));

        const list = this._document.createElement('ol');
        list.className = 'form-designer__field-list';
        this._fields.slice().sort((left, right) => left.order - right.order)
            .forEach((field) => list.appendChild(this._createFieldRow(field)));
        schema.appendChild(list);

        const workspace = this._document.createElement('section');
        workspace.className = 'form-designer__workspace';
        workspace.setAttribute('aria-label', '表單設計畫布');
        workspace.appendChild(this._createPanelHeader('12 欄表單畫布', false));
        workspace.appendChild(this._createCanvas());
        this.element.append(schema, workspace);
        this._applySelection();
    }

    _createPanelHeader(title, withAdd) {
        const header = this._document.createElement('header');
        header.className = 'form-designer__panel-header';
        const heading = this._document.createElement('h2');
        heading.className = 'form-designer__panel-title';
        heading.textContent = title;
        header.appendChild(heading);
        if (withAdd) {
            const add = this._track(new BasicButton({
                type: 'addRow',
                variant: 'secondary',
                customLabel: '新增欄位',
                onClick: () => this.addField(),
            }));
            add.mount(header);
        }
        return header;
    }

    _createFieldRow(field) {
        const row = this._document.createElement('li');
        row.className = 'form-designer__field-row';
        row.dataset.fieldId = field.field_id;
        this._rowById.set(field.field_id, row);
        this._listen(row, 'click', () => this.setSelectedId(field.field_id));

        const dragHost = this._document.createElement('span');
        dragHost.className = 'form-designer__drag-handle';
        dragHost.draggable = true;
        dragHost.title = '拖曳至表單畫布';
        dragHost.setAttribute('aria-label', `拖曳 ${field.display_name}`);
        const dragIcon = this._track(new Icon({ name: 'touch-app', size: 18, color: 'currentColor' }));
        dragIcon.mount(dragHost);
        this._listen(dragHost, 'dragstart', (event) => {
            this._dragFieldId = field.field_id;
            event.dataTransfer?.setData('text/plain', field.field_id);
            if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
        });
        this._listen(dragHost, 'dragend', () => { this._dragFieldId = null; });
        row.appendChild(dragHost);

        const editors = this._document.createElement('div');
        editors.className = 'form-designer__field-editors';
        const columnInput = this._track(new TextInput({
            label: '欄位名稱',
            value: field.column_name,
            size: 'small',
            enableSecurity: false,
            onBlur: (value) => this._updateFieldText(field.field_id, 'column_name', value),
        }));
        columnInput.mount(editors);
        const displayInput = this._track(new TextInput({
            label: '顯示名稱',
            value: field.display_name,
            size: 'small',
            enableSecurity: false,
            onBlur: (value) => this._updateFieldText(field.field_id, 'display_name', value),
        }));
        displayInput.mount(editors);

        const pickerHost = this._document.createElement('div');
        pickerHost.className = 'form-designer__component-picker';
        const picker = this._track(new Dropdown({
            variant: 'searchable',
            items: componentItems(),
            value: field.input.component,
            size: 'small',
            onChange: (value) => this._setFieldComponent(field.field_id, value),
        }));
        picker.mount(pickerHost);
        editors.appendChild(pickerHost);
        const meta = this._document.createElement('span');
        meta.className = 'form-designer__field-meta';
        meta.textContent = `${field.db_type} · ${field.layout.column_span}/${this.columns} 欄`;
        editors.appendChild(meta);
        row.appendChild(editors);

        const iconHost = this._document.createElement('span');
        iconHost.className = 'form-designer__field-icon';
        iconHost.title = '切換下一個輸入元件';
        const fieldIcon = this._track(new Icon({
            name: Icon.has(field.icon) ? field.icon : FORM_DESIGNER_COMPONENT_ICONS[field.input.component],
            size: 18,
            color: 'currentColor',
            title: `切換 ${field.display_name} 的輸入元件`,
            onClick: () => this._cycleFieldComponent(field.field_id),
        }));
        fieldIcon.mount(iconHost);
        row.appendChild(iconHost);
        return row;
    }

    _createCanvas() {
        const canvas = this._document.createElement('div');
        canvas.className = 'form-designer__canvas';
        canvas.style.setProperty('grid-template-columns', `repeat(${this.columns}, minmax(0, 1fr))`);
        this._listen(canvas, 'dragover', (event) => {
            event.preventDefault();
            canvas.classList.add('form-designer__canvas--dragover');
            if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
        });
        this._listen(canvas, 'dragleave', (event) => {
            if (!canvas.contains(event.relatedTarget)) canvas.classList.remove('form-designer__canvas--dragover');
        });
        this._listen(canvas, 'drop', (event) => {
            event.preventDefault();
            canvas.classList.remove('form-designer__canvas--dragover');
            const fieldId = this._dragFieldId || event.dataTransfer?.getData('text/plain');
            if (!fieldId) return;
            const target = this._pointToGrid(canvas, event.clientX, event.clientY);
            this._commitLayout(tryMoveFormDesignerField(this._fields, fieldId, target, this.columns), fieldId);
            this._dragFieldId = null;
        });

        if (this._fields.length === 0) {
            const empty = this._document.createElement('p');
            empty.className = 'form-designer__empty';
            empty.textContent = '從左側新增或拖曳欄位到這裡。';
            canvas.appendChild(empty);
            return canvas;
        }
        this._fields.forEach((field) => canvas.appendChild(this._createTile(field, canvas)));
        return canvas;
    }

    _createTile(field, canvas) {
        const tile = this._document.createElement('article');
        tile.className = 'form-designer__tile';
        tile.dataset.fieldId = field.field_id;
        tile.tabIndex = 0;
        tile.setAttribute('aria-label', `${field.display_name}，Alt 加方向鍵移動，Shift 加方向鍵調整大小`);
        tile.style.setProperty('grid-column', `${field.layout.column} / span ${field.layout.column_span}`);
        tile.style.setProperty('grid-row', `${field.layout.row} / span ${field.layout.row_span}`);
        this._tileById.set(field.field_id, tile);
        this._listen(tile, 'click', () => this.setSelectedId(field.field_id));
        this._listen(tile, 'keydown', (event) => this._handleTileKeydown(event, field.field_id));

        const header = this._document.createElement('header');
        header.className = 'form-designer__tile-header';
        const dragHost = this._document.createElement('span');
        dragHost.className = 'form-designer__tile-drag';
        dragHost.title = '拖曳欄位';
        const dragIcon = this._track(new Icon({ name: 'touch-app', size: 17, color: 'currentColor' }));
        dragIcon.mount(dragHost);
        this._listen(dragHost, 'pointerdown', (event) => this._startPointerMove(event, field.field_id, tile, canvas));
        header.appendChild(dragHost);

        const icon = this._track(new Icon({
            name: Icon.has(field.icon) ? field.icon : FORM_DESIGNER_COMPONENT_ICONS[field.input.component],
            size: 18,
            color: 'currentColor',
            title: '切換下一個輸入元件',
            onClick: () => this._cycleFieldComponent(field.field_id),
        }));
        icon.mount(header);
        const title = this._document.createElement('span');
        title.className = 'form-designer__tile-title';
        title.textContent = field.display_name || field.column_name;
        header.appendChild(title);
        const remove = this._track(new Icon({
            name: 'delete',
            size: 17,
            color: 'var(--cl-danger)',
            title: `刪除 ${field.display_name}`,
            onClick: () => this.removeField(field.field_id),
        }));
        remove.mount(header);
        tile.appendChild(header);

        const preview = this._document.createElement('div');
        preview.className = 'form-designer__tile-preview';
        this._createPreview(field).mount(preview);
        tile.appendChild(preview);

        const resize = this._document.createElement('button');
        resize.type = 'button';
        resize.className = 'form-designer__resize-handle';
        resize.textContent = '↘';
        resize.title = '拖曳調整寬高；方向鍵調整大小';
        resize.setAttribute('aria-label', `調整 ${field.display_name} 大小`);
        this._listen(resize, 'pointerdown', (event) => this._startPointerResize(event, field.field_id, tile, canvas));
        this._listen(resize, 'keydown', (event) => this._handleResizeKeydown(event, field.field_id));
        tile.appendChild(resize);
        return tile;
    }

    _createPreview(field) {
        const common = { disabled: true, readonly: true, width: '100%' };
        let component;
        switch (field.input.component) {
            case 'TextArea':
                component = new TextArea({ ...common, value: '', rows: 2, resize: 'none', placeholder: field.display_name });
                break;
            case 'NumberInput':
                component = new NumberInput({ ...common, value: null, placeholder: field.display_name });
                break;
            case 'Dropdown':
                component = new Dropdown({ ...common, items: [], placeholder: field.display_name, size: 'small' });
                break;
            case 'Checkbox':
                component = new Checkbox({ disabled: true, label: field.display_name, checked: false });
                break;
            case 'ToggleSwitch':
                component = new ToggleSwitch({ disabled: true, label: field.display_name, checked: false });
                break;
            case 'DatePicker':
                component = new DatePicker({ disabled: true, placeholder: field.display_name });
                break;
            default:
                component = new TextInput({ ...common, value: '', size: 'small', placeholder: field.display_name });
                break;
        }
        return this._track(component);
    }

    _pointToGrid(canvas, clientX, clientY) {
        const rect = canvas.getBoundingClientRect();
        const columnWidth = rect.width / this.columns || 1;
        const rowHeight = this._rowHeight(canvas);
        return {
            column: Math.min(this.columns, Math.max(1, Math.floor((clientX - rect.left) / columnWidth) + 1)),
            row: Math.max(1, Math.floor((clientY - rect.top + canvas.scrollTop) / rowHeight) + 1),
        };
    }

    _rowHeight(canvas) {
        const view = this._document?.defaultView || globalThis;
        const value = Number.parseFloat(view.getComputedStyle(canvas).getPropertyValue('--form-designer-row-height'));
        return Number.isFinite(value) && value > 0 ? value : 92;
    }

    _startPointerMove(event, fieldId, tile, canvas) {
        if (event.button !== 0) return;
        event.preventDefault();
        this.setSelectedId(fieldId);
        const field = selectedField(this._fields, fieldId);
        if (!field) return;
        const startX = event.clientX;
        const startY = event.clientY;
        const original = { ...field.layout };
        const rect = canvas.getBoundingClientRect();
        const columnWidth = rect.width / this.columns || 1;
        const rowHeight = this._rowHeight(canvas);
        const pointerTarget = event.currentTarget;
        tile.classList.add('form-designer__tile--pointer-active');
        pointerTarget.setPointerCapture?.(event.pointerId);
        const move = (moveEvent) => {
            tile.style.setProperty('transform', `translate(${moveEvent.clientX - startX}px, ${moveEvent.clientY - startY}px)`);
        };
        const finish = (finishEvent) => {
            cleanup();
            const column = original.column + Math.round((finishEvent.clientX - startX) / columnWidth);
            const row = original.row + Math.round((finishEvent.clientY - startY) / rowHeight);
            this._commitLayout(tryMoveFormDesignerField(this._fields, fieldId, { column, row }, this.columns), fieldId);
        };
        const cleanup = () => {
            pointerTarget.removeEventListener('pointermove', move);
            pointerTarget.removeEventListener('pointerup', finish);
            pointerTarget.removeEventListener('pointercancel', cancel);
            tile.classList.remove('form-designer__tile--pointer-active');
            tile.style.removeProperty('transform');
            this._pointerCleanup = null;
        };
        const cancel = () => cleanup();
        this._pointerCleanup?.();
        this._pointerCleanup = cleanup;
        pointerTarget.addEventListener('pointermove', move);
        pointerTarget.addEventListener('pointerup', finish);
        pointerTarget.addEventListener('pointercancel', cancel);
    }

    _startPointerResize(event, fieldId, tile, canvas) {
        if (event.button !== 0) return;
        event.preventDefault();
        event.stopPropagation();
        this.setSelectedId(fieldId);
        const field = selectedField(this._fields, fieldId);
        if (!field) return;
        const startX = event.clientX;
        const startY = event.clientY;
        const original = { ...field.layout };
        const columnWidth = canvas.getBoundingClientRect().width / this.columns || 1;
        const rowHeight = this._rowHeight(canvas);
        const pointerTarget = event.currentTarget;
        tile.classList.add('form-designer__tile--pointer-active');
        pointerTarget.setPointerCapture?.(event.pointerId);
        const move = (moveEvent) => {
            const columnSpan = Math.max(1, original.column_span + Math.round((moveEvent.clientX - startX) / columnWidth));
            const rowSpan = Math.max(1, original.row_span + Math.round((moveEvent.clientY - startY) / rowHeight));
            tile.style.setProperty('grid-column', `${original.column} / span ${columnSpan}`);
            tile.style.setProperty('grid-row', `${original.row} / span ${rowSpan}`);
        };
        const finish = (finishEvent) => {
            cleanup();
            const columnSpan = original.column_span + Math.round((finishEvent.clientX - startX) / columnWidth);
            const rowSpan = original.row_span + Math.round((finishEvent.clientY - startY) / rowHeight);
            this._commitLayout(tryResizeFormDesignerField(
                this._fields, fieldId, { column_span: columnSpan, row_span: rowSpan }, this.columns,
            ), fieldId);
        };
        const cleanup = () => {
            pointerTarget.removeEventListener('pointermove', move);
            pointerTarget.removeEventListener('pointerup', finish);
            pointerTarget.removeEventListener('pointercancel', cancel);
            tile.classList.remove('form-designer__tile--pointer-active');
            this._pointerCleanup = null;
        };
        const cancel = () => {
            tile.style.setProperty('grid-column', `${original.column} / span ${original.column_span}`);
            tile.style.setProperty('grid-row', `${original.row} / span ${original.row_span}`);
            cleanup();
        };
        this._pointerCleanup?.();
        this._pointerCleanup = cleanup;
        pointerTarget.addEventListener('pointermove', move);
        pointerTarget.addEventListener('pointerup', finish);
        pointerTarget.addEventListener('pointercancel', cancel);
    }

    _handleTileKeydown(event, fieldId) {
        const field = selectedField(this._fields, fieldId);
        if (!field) return;
        const directions = {
            ArrowLeft: { column: field.layout.column - 1, row: field.layout.row },
            ArrowRight: { column: field.layout.column + 1, row: field.layout.row },
            ArrowUp: { column: field.layout.column, row: field.layout.row - 1 },
            ArrowDown: { column: field.layout.column, row: field.layout.row + 1 },
        };
        if (event.altKey && directions[event.key]) {
            event.preventDefault();
            this._commitLayout(tryMoveFormDesignerField(this._fields, fieldId, directions[event.key], this.columns), fieldId);
            return;
        }
        if (event.shiftKey && directions[event.key]) this._handleResizeKeydown(event, fieldId);
    }

    _handleResizeKeydown(event, fieldId) {
        const field = selectedField(this._fields, fieldId);
        if (!field || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
        event.preventDefault();
        event.stopPropagation();
        const size = {
            column_span: field.layout.column_span + (event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0),
            row_span: field.layout.row_span + (event.key === 'ArrowDown' ? 1 : event.key === 'ArrowUp' ? -1 : 0),
        };
        this._commitLayout(tryResizeFormDesignerField(this._fields, fieldId, size, this.columns), fieldId);
    }

    _commitLayout(result, fieldId) {
        if (!result.accepted) {
            this._render();
            this.setSelectedId(fieldId);
            return false;
        }
        this._fields = result.fields;
        this._selectedId = fieldId;
        this._render();
        this._emitChange('layout', fieldId);
        return true;
    }

    _updateFieldText(fieldId, key, value) {
        const field = selectedField(this._fields, fieldId);
        if (!field) return false;
        const candidate = String(value ?? '').trim();
        const validColumnName = /^[A-Za-z][A-Za-z0-9_]{0,62}$/.test(candidate)
            && !this._fields.some((entry) => (
                entry.field_id !== fieldId
                && entry.column_name.toLocaleLowerCase() === candidate.toLocaleLowerCase()
            ));
        const validDisplayName = candidate.length > 0 && !/[<>]/.test(candidate);
        if ((key === 'column_name' && !validColumnName) || (key === 'display_name' && !validDisplayName)) {
            this._render();
            this.setSelectedId(fieldId);
            return false;
        }
        field[key] = candidate;
        if (key === 'display_name') {
            const title = this._tileById.get(fieldId)?.querySelector('.form-designer__tile-title');
            if (title) title.textContent = field.display_name || field.column_name;
        }
        this._emitChange('field', fieldId);
        return true;
    }

    _setFieldComponent(fieldId, component) {
        if (!FORM_DESIGNER_COMPONENTS.includes(component)) return false;
        const field = selectedField(this._fields, fieldId);
        if (!field) return false;
        field.input.component = component;
        field.input.field_type = FORM_DESIGNER_COMPONENT_FIELD_TYPES[component];
        field.input.options ||= {};
        field.icon = FORM_DESIGNER_COMPONENT_ICONS[component];
        this._selectedId = fieldId;
        this._render();
        this._emitChange('component', fieldId);
        return true;
    }

    _cycleFieldComponent(fieldId) {
        const field = selectedField(this._fields, fieldId);
        if (!field) return false;
        const index = FORM_DESIGNER_COMPONENTS.indexOf(field.input.component);
        return this._setFieldComponent(fieldId, FORM_DESIGNER_COMPONENTS[(index + 1) % FORM_DESIGNER_COMPONENTS.length]);
    }

    _emitChange(reason, fieldId = null) {
        if (typeof this.options.onChange === 'function') {
            this.options.onChange(this.getValue(), { reason, field_id: fieldId });
        }
    }

    _emitSelect() {
        if (typeof this.options.onSelect === 'function') {
            const field = selectedField(this._fields, this._selectedId);
            this.options.onSelect(field ? cloneFormDesignerFields([field])[0] : null);
        }
    }

    _applySelection() {
        for (const [fieldId, row] of this._rowById) {
            row.classList.toggle('form-designer__field-row--selected', fieldId === this._selectedId);
        }
        for (const [fieldId, tile] of this._tileById) {
            const active = fieldId === this._selectedId;
            tile.classList.toggle('form-designer__tile--selected', active);
            tile.setAttribute('aria-selected', active ? 'true' : 'false');
        }
    }

    getValue() {
        return {
            ...cloneFormDesignerJson(this._definitionBase),
            fields: cloneFormDesignerFields(this._fields),
        };
    }

    setValue(value) {
        this._definitionBase = definitionBase(value);
        this._fields = normalizeFormDesignerFields(definitionFields(value), this.columns);
        this._selectedId = this._fields[0]?.field_id || null;
        this._render();
        return this;
    }

    setSchema(schema) {
        return this.setValue(Array.isArray(schema) ? { fields: schema } : schema);
    }

    addField(field = {}) {
        const index = this._fields.length;
        const identifiers = new Set(this._fields.map((entry) => entry.field_id));
        let sequence = index + 1;
        let generatedId = `field_${sequence}`;
        while (identifiers.has(generatedId)) generatedId = `field_${++sequence}`;
        const raw = {
            nullable: true,
            primary_key: false,
            identity: false,
            default: null,
            input: { field_type: 'text', component: 'TextInput', options: {} },
            validation: { required: false },
            ...field,
            field_id: field.field_id ?? generatedId,
            column_name: field.column_name ?? `new_field_${index + 1}`,
            display_name: field.display_name ?? `新增欄位 ${index + 1}`,
            db_type: field.db_type ?? 'text',
            layout: field.layout ?? { row: 1, column: 1, column_span: this.columns, row_span: 1 },
            order: field.order ?? index + 1,
        };
        for (const key of Object.keys(raw)) {
            if (raw[key] === undefined) delete raw[key];
        }
        this._fields = normalizeFormDesignerFields([...this._fields, raw], this.columns);
        const added = this._fields.find((entry) => entry.field_id === raw.field_id)
            || this._fields.at(-1);
        this._selectedId = added?.field_id || null;
        this._render();
        this._emitChange('add', this._selectedId);
        this._emitSelect();
        return added ? cloneFormDesignerFields([added])[0] : null;
    }

    removeField(fieldId) {
        const before = this._fields.length;
        this._fields = packFormDesignerFields(
            this._fields.filter((field) => field.field_id !== fieldId),
            this.columns,
        );
        if (this._fields.length === before) return false;
        if (this._selectedId === fieldId) this._selectedId = this._fields[0]?.field_id || null;
        this._render();
        this._emitChange('remove', fieldId);
        this._emitSelect();
        return true;
    }

    setSelectedId(fieldId) {
        const next = selectedField(this._fields, fieldId) ? fieldId : null;
        if (next === this._selectedId) return this;
        this._selectedId = next;
        this._applySelection();
        this._emitSelect();
        return this;
    }

    destroy() {
        this._clearRendered();
        this.element?.remove();
        this.element = null;
        this._container = null;
        this._document = null;
        this._fields = [];
        this._definitionBase = {};
        this._selectedId = null;
    }
}

export default FormDesigner;
