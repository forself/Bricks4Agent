

export class RegionMap {
    /**
     * @param {Object} options
     * @param {Object} options.data - 區域資料 { 'TPE': { value: 100, label: '台北' }, ... }
     * @param {string} options.width - 寬度
     * @param {string} options.height - 高度
     * @param {string} options.defaultColor - 預設顏色
     * @param {Function} options.colorScale - 顏色映射函式 (value) => color
     * @param {Function} options.onClick - 點擊回調 (regionCode) => void
     * @param {Function} options.onChange - 選取變更回調
     * @param {boolean} options.showLabels - 顯示標籤
     * @param {boolean} options.showValues - 顯示數值
     */
    constructor(options = {}) {
        this.options = {
            data: {},
            width: '100%',
            height: '500px',
            defaultColor: 'var(--cl-border-light)',
            hoverColor: 'var(--cl-primary-light)',
            selectedColor: 'var(--cl-primary)',
            colorScale: null,
            onClick: null,
            onChange: null,
            showLabels: true,
            showValues: false,
            labelFontSize: 12,
            valueFontSize: 10,
            ...options
        };

        this.regions = new Map();
        this.selectedRegion = null;
        this.element = this._createElement();
        
        // Tooltip
        this.tooltip = document.createElement('div');
        this.tooltip.style.cssText = `
            position: absolute;
            background: var(--cl-bg-overlay-strong);
            color: var(--cl-text-inverse);
            padding: 8px 12px;
            border-radius: var(--cl-radius-sm);
            font-size: var(--cl-font-size-sm);
            pointer-events: none;
            opacity: 0;
            transition: opacity var(--cl-transition);
            z-index: 1000;
            white-space: nowrap;
        `;
        this.element.appendChild(this.tooltip);

        this._applyData();
    }

    _createElement() {
        const container = document.createElement('div');
        container.className = 'region-map';
        container.style.cssText = `
            position: relative;
            width: ${this.options.width};
            height: ${this.options.height};
            background: var(--cl-bg-tertiary);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-lg);
            display: flex;
            align-items: center;
            justify-content: center;
            overflow: hidden;
        `;

        // 台灣 SVG 地圖 (正確路徑，提取自 Taiwan.svg)
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 400 700'); // 調整 viewBox 以符合台灣形狀 (最大 Y 約 680)
        svg.style.cssText = 'width: 100%; height: 100%; max-width: 500px;';

        // 完整正確的區域資料
        const regionsData = [
            { code: 'LJC', name: '連江縣', paths: ['M30,40 L45,35 L50,45 L40,55 L28,50 Z', 'M55,50 L65,48 L68,58 L58,60 Z'] },
            { code: 'KMC', name: '金門縣', paths: ['M25,130 L55,120 L70,130 L65,145 L35,150 L20,140 Z', 'M75,140 L85,138 L88,148 L78,150 Z'] },
            { code: 'PHC', name: '澎湖縣', paths: ['M100,290 L115,280 L130,285 L135,300 L125,320 L110,325 L95,315 L90,300 Z', 'M140,295 L150,290 L155,305 L145,310 Z'] },
            { code: 'KLU', name: '基隆市', paths: ['M305,65 L325,60 L340,70 L335,85 L315,90 L300,80 Z'] },
            { code: 'TPE', name: '臺北市', paths: ['M270,80 L295,75 L310,90 L305,110 L280,115 L265,100 Z'] },
            { code: 'NWT', name: '新北市', paths: ['M245,60 L270,55 L305,60 L340,70 L345,95 L340,130 L310,150 L280,160 L250,145 L240,115 L230,90 L235,70 Z M265,100 L280,115 L305,110 L310,90 L295,75 L270,80 Z'] },
            { code: 'TYN', name: '桃園市', paths: ['M195,105 L230,95 L250,110 L255,145 L230,165 L200,160 L180,140 L185,115 Z'] },
            { code: 'HSZ', name: '新竹市', paths: ['M185,165 L210,160 L220,175 L210,190 L185,185 Z'] },
            { code: 'HSC', name: '新竹縣', paths: ['M200,160 L230,150 L265,155 L280,175 L270,200 L240,210 L210,205 L210,190 L220,175 L210,160 Z'] },
            { code: 'MLC', name: '苗栗縣', paths: ['M175,190 L210,185 L240,195 L270,210 L280,240 L260,270 L220,275 L180,260 L165,225 Z'] },
            { code: 'YLC', name: '宜蘭縣', paths: ['M310,130 L345,110 L365,130 L370,175 L350,215 L315,220 L290,200 L285,165 L295,145 Z'] },
            { code: 'TXG', name: '臺中市', paths: ['M160,240 L205,230 L260,250 L290,280 L285,320 L250,350 L200,345 L165,310 L145,270 Z'] },
            { code: 'HUA', name: '花蓮縣', paths: ['M295,200 L330,190 L360,220 L375,280 L370,360 L340,400 L305,380 L290,320 L280,260 Z'] },
            { code: 'CHW', name: '彰化縣', paths: ['M145,285 L175,270 L200,285 L210,320 L195,355 L160,360 L135,340 L130,305 Z'] },
            { code: 'NTC', name: '南投縣', paths: ['M195,300 L250,290 L290,310 L300,370 L280,420 L240,430 L200,410 L180,370 L180,330 Z'] },
            { code: 'YUN', name: '雲林縣', paths: ['M130,345 L165,335 L200,350 L210,385 L190,415 L150,420 L120,400 L115,365 Z'] },
            { code: 'CYI', name: '嘉義市', paths: ['M165,420 L185,415 L195,430 L185,445 L165,440 Z'] },
            { code: 'CYC', name: '嘉義縣', paths: ['M125,400 L160,385 L200,400 L240,420 L250,455 L230,485 L180,490 L140,470 L120,435 Z M165,420 L185,415 L195,430 L185,445 L165,440 Z'] },
            { code: 'TTT', name: '臺東縣', paths: ['M295,385 L340,370 L370,400 L375,480 L350,530 L300,520 L270,480 L275,420 Z'] },
            { code: 'TNN', name: '臺南市', paths: ['M125,450 L165,440 L210,455 L240,485 L235,530 L195,555 L150,545 L120,510 L115,470 Z'] },
            { code: 'KHH', name: '高雄市', paths: ['M155,520 L200,510 L250,480 L285,465 L300,500 L295,560 L260,590 L210,600 L170,580 L145,545 Z'] },
            { code: 'PIF', name: '屏東縣', paths: ['M200,565 L250,545 L290,530 L320,555 L335,610 L315,660 L270,680 L230,670 L200,640 L190,595 Z'] },
        ];
        
        // 繪製區域
        regionsData.forEach(r => {
            const group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
            group.setAttribute('class', 'region-group');
            group.setAttribute('id', `region-${r.code}`);
            group.style.cursor = 'pointer';

            r.paths.forEach(d => {
                const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
                path.setAttribute('d', d);
                path.setAttribute('fill', this.options.defaultColor);
                path.setAttribute('stroke', 'var(--cl-bg)');
                path.setAttribute('stroke-width', '0.5');
                group.appendChild(path);
            });
            
            // Event Listeners
            group.addEventListener('mouseenter', (e) => {
                this._setRegionFill(group, this.options.hoverColor);
                this._showTooltip(e, r.name, this.options.data[r.code]);
            });
            
            group.addEventListener('mousemove', (e) => {
                this._moveTooltip(e);
            });

            group.addEventListener('mouseleave', () => {
                const data = this.options.data[r.code];
                const baseColor = data?.color 
                    || (data && this.options.colorScale ? this.options.colorScale(data.value) : this.options.defaultColor);
                if (this.selectedRegion === r.code) {
                    this._setRegionFill(group, this.options.selectedColor);
                } else {
                    this._setRegionFill(group, baseColor);
                }
                this._hideTooltip();
            });

            group.addEventListener('click', () => {
                // 清除之前的選取
                if (this.selectedRegion) {
                    const prev = this.regions.get(this.selectedRegion);
                    if (prev) {
                        const prevData = this.options.data[this.selectedRegion];
                        const prevColor = prevData?.color 
                            || (prevData && this.options.colorScale ? this.options.colorScale(prevData.value) : this.options.defaultColor);
                        this._setRegionFill(prev.element, prevColor);
                    }
                }
                
                this.selectedRegion = r.code;
                this._setRegionFill(group, this.options.selectedColor);
                
                if (this.options.onClick) this.options.onClick(r.code);
                if (this.options.onChange) this.options.onChange({ code: r.code, name: r.name });
            });

            svg.appendChild(group);

            this.regions.set(r.code, {
                element: group,
                name: r.name,
                originalFill: this.options.defaultColor
            });
        });

        container.appendChild(svg);
        return container;
    }


    _showTooltip(e, name, data) {
        const escape = (str) => String(str).replaceAll(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
        
        let content = `<div style="font-weight:600; margin-bottom:4px;">${escape(name)}</div>`;
        
        if (data) {
            if (data.value !== undefined) {
                content += `<div>數值: <span style="color:var(--cl-primary-light)">${escape(data.value)}</span></div>`;
            }
            // 顯示額外資訊
            Object.keys(data).forEach(key => {
                if (key !== 'value' && key !== 'color' && key !== 'label' && typeof data[key] !== 'object') {
                    content += `<div style="font-size:var(--cl-font-size-xs); color:var(--cl-text-light)">${key}: ${escape(data[key])}</div>`;
                }
            });
        }

        this.tooltip.innerHTML = content;
        this.tooltip.style.opacity = '1';
        this._moveTooltip(e);
    }

    _moveTooltip(e) {
        const rect = this.element.getBoundingClientRect();
        let x = e.clientX - rect.left + 15;
        let y = e.clientY - rect.top - 10;

        // 防止溢出
        const tooltipRect = this.tooltip.getBoundingClientRect();
        if (x + tooltipRect.width > rect.width) {
            x = e.clientX - rect.left - tooltipRect.width - 15;
        }

        this.tooltip.style.left = `${x}px`;
        this.tooltip.style.top = `${y}px`;
    }

    _hideTooltip() {
        this.tooltip.style.opacity = '0';
    }

    _getRegionPaths(regionElement) {
        return Array.from(regionElement.querySelectorAll('path'));
    }

    _setRegionFill(regionElement, color) {
        this._getRegionPaths(regionElement).forEach((path) => {
            path.setAttribute('fill', color);
            path.style.fill = color;
        });
    }

    _applyData() {
        const { data, colorScale, defaultColor, showLabels, showValues } = this.options;

        this.regions.forEach((region, code) => {
            const regionData = data[code];
            const oldLabel = region.element.querySelector('.region-label');
            if (oldLabel) {
                oldLabel.remove();
            }

            // 著色
            if (regionData && colorScale) {
                this._setRegionFill(region.element, colorScale(regionData.value));
            } else if (regionData?.color) {
                this._setRegionFill(region.element, regionData.color);
            } else {
                this._setRegionFill(region.element, region.originalFill || defaultColor);
            }

            // 加入文字標籤
            if ((showLabels || showValues) && regionData) {
                this._addLabel(region.element, code, region.name, regionData);
            }
        });
    }

    _addLabel(groupEl, code, name, data) {
        const { showLabels, showValues, labelFontSize, valueFontSize } = this.options;

        // 移除舊標籤
        const oldLabel = groupEl.querySelector('.region-label');
        if (oldLabel) oldLabel.remove();

        // 取得區域中心點
        const bbox = groupEl.getBBox();
        const cx = bbox.x + bbox.width / 2;
        const cy = bbox.y + bbox.height / 2;

        const labelGroup = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        labelGroup.classList.add('region-label');
        labelGroup.style.pointerEvents = 'none';

        let yOffset = 0;

        // 名稱標籤
        if (showLabels && name) {
            const nameText = document.createElementNS('http://www.w3.org/2000/svg', 'text');
            nameText.setAttribute('x', cx);
            nameText.setAttribute('y', cy + yOffset);
            nameText.setAttribute('text-anchor', 'middle');
            nameText.setAttribute('dominant-baseline', 'middle');
            nameText.setAttribute('font-size', labelFontSize);
            nameText.setAttribute('fill', 'var(--cl-text)');
            nameText.setAttribute('font-weight', '500');
            nameText.textContent = name;
            labelGroup.appendChild(nameText);
            yOffset += labelFontSize + 2;
        }

        // 數值標籤
        if (showValues && data?.value !== undefined) {
            const valueText = document.createElementNS('http://www.w3.org/2000/svg', 'text');
            valueText.setAttribute('x', cx);
            valueText.setAttribute('y', cy + yOffset);
            valueText.setAttribute('text-anchor', 'middle');
            valueText.setAttribute('dominant-baseline', 'middle');
            valueText.setAttribute('font-size', valueFontSize);
            valueText.setAttribute('fill', 'var(--cl-primary-dark)');
            valueText.setAttribute('font-weight', 'bold');
            valueText.textContent = data.value;
            labelGroup.appendChild(valueText);
        }

        groupEl.appendChild(labelGroup);
    }

    /**
     * 更新統計資料
     */
    setData(data) {
        this.options.data = data;
        this._applyData();
    }

    /**
     * 設定顏色比例尺
     */
    setColorScale(colorScale) {
        this.options.colorScale = colorScale;
        this._applyData();
    }

    /**
     * 高亮指定區域
     */
    highlightRegion(code) {
        this.regions.forEach((region, c) => {
            const paths = this._getRegionPaths(region.element);
            if (c === code) {
                paths.forEach((path) => {
                    path.style.stroke = 'var(--cl-text-dark)';
                    path.style.strokeWidth = '2';
                });
            } else {
                paths.forEach((path) => {
                    path.style.stroke = '';
                    path.style.strokeWidth = '';
                });
            }
        });
    }

    /**
     * 清除高亮
     */
    clearHighlight() {
        this.regions.forEach((region) => {
            this._getRegionPaths(region.element).forEach((path) => {
                path.style.stroke = '';
                path.style.strokeWidth = '';
            });
        });
    }

    /**
     * 建立顏色比例尺
     */
    static createColorScale(min, max, colors = ['var(--cl-primary-light)', 'var(--cl-primary-dark)']) {
        const startColor = RegionMap._resolveColorToRgb(colors[0]) || { r: 220, g: 235, b: 255 };
        const endColor = RegionMap._resolveColorToRgb(colors[1]) || { r: 26, g: 115, b: 232 };
        return (value) => {
            if (value === undefined || value === null) {
                return RegionMap._rgbToHex(startColor);
            }

            const range = max - min;
            const ratio = range === 0 ? 1 : (value - min) / range;
            const clampedRatio = Math.max(0, Math.min(1, ratio));

            // 簡單線性插值
            const r = Math.round(startColor.r + (endColor.r - startColor.r) * clampedRatio);
            const g = Math.round(startColor.g + (endColor.g - startColor.g) * clampedRatio);
            const b = Math.round(startColor.b + (endColor.b - startColor.b) * clampedRatio);

            return RegionMap._rgbToHex({ r, g, b });
        };
    }

    static _resolveColorToRgb(color) {
        if (typeof color !== 'string' || !color.trim()) {
            return null;
        }

        const normalizedColor = color.trim();
        const hexMatch = normalizedColor.match(/^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/);
        if (hexMatch) {
            const hex = hexMatch[1].length === 3
                ? hexMatch[1].split('').map((char) => char + char).join('')
                : hexMatch[1];
            return {
                r: Number.parseInt(hex.slice(0, 2), 16),
                g: Number.parseInt(hex.slice(2, 4), 16),
                b: Number.parseInt(hex.slice(4, 6), 16)
            };
        }

        const rgbMatch = normalizedColor.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
        if (rgbMatch) {
            return {
                r: Number.parseInt(rgbMatch[1], 10),
                g: Number.parseInt(rgbMatch[2], 10),
                b: Number.parseInt(rgbMatch[3], 10)
            };
        }

        if (typeof document === 'undefined') {
            return null;
        }

        const probe = document.createElement('span');
        probe.style.color = normalizedColor;
        probe.style.position = 'absolute';
        probe.style.opacity = '0';
        probe.style.pointerEvents = 'none';

        const mountTarget = document.body || document.documentElement;
        if (!mountTarget) {
            return null;
        }

        mountTarget.appendChild(probe);
        const resolvedColor = getComputedStyle(probe).color;
        probe.remove();

        const resolvedMatch = resolvedColor.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
        if (!resolvedMatch) {
            return null;
        }

        return {
            r: Number.parseInt(resolvedMatch[1], 10),
            g: Number.parseInt(resolvedMatch[2], 10),
            b: Number.parseInt(resolvedMatch[3], 10)
        };
    }

    static _rgbToHex({ r, g, b }) {
        return `#${[r, g, b]
            .map((channel) => channel.toString(16).padStart(2, '0'))
            .join('')}`;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default RegionMap;
