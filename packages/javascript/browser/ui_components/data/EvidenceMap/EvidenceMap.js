const asArray = (value) => Array.isArray(value) ? value : [];

/**
 * EvidenceMap - 無第三方相依的案件座標圖。
 * 使用後端既有 TWD97 座標計算相對位置，並保留鍵盤可操作的標記與明細連結。
 */
export class EvidenceMap {
    constructor(options = {}) {
        this.options = {
            data: [],
            height: '430px',
            emptyText: '無可定位資料',
            linkBuilder: null,
            ...options,
        };
        this.element = document.createElement('div');
        this.element.className = 'b4a-evidence-map';
        this.element.style.height = this.options.height;
        this.element.setAttribute('role', 'region');
        this.element.setAttribute('aria-label', '事證分布地圖');
        this._render();
    }

    setData(data) {
        this.options.data = asArray(data);
        this._render();
    }

    _validRows() {
        return asArray(this.options.data).filter((row) => {
            const address = row?.Addr;
            return address?.ExpireDate && Number.isFinite(Number(address.lon)) && Number.isFinite(Number(address.lat));
        });
    }

    _render() {
        this.element.replaceChildren();
        const valid = this._validRows();
        const backdrop = document.createElement('img');
        backdrop.className = 'b4a-evidence-map__backdrop';
        backdrop.src = new URL('../RegionMap/maps/Taiwan.svg', import.meta.url).href;
        backdrop.alt = '';
        this.element.append(backdrop);

        if (!valid.length) {
            const empty = document.createElement('p');
            empty.className = 'b4a-evidence-map__empty';
            empty.textContent = this.options.emptyText;
            this.element.append(empty);
            return;
        }

        const xs = valid.map((row) => Number(row.Addr.lon));
        const ys = valid.map((row) => Number(row.Addr.lat));
        const minX = Math.min(...xs);
        const maxX = Math.max(...xs);
        const minY = Math.min(...ys);
        const maxY = Math.max(...ys);
        const rangeX = maxX - minX || 1;
        const rangeY = maxY - minY || 1;

        for (const row of valid) {
            const marker = document.createElement('button');
            marker.type = 'button';
            marker.className = 'b4a-evidence-map__marker';
            marker.style.left = `${12 + ((Number(row.Addr.lon) - minX) / rangeX) * 76}%`;
            marker.style.top = `${88 - ((Number(row.Addr.lat) - minY) / rangeY) * 76}%`;
            marker.title = `${row.Name || '未命名案件'}｜${row.Addr.Address || ''}`;
            marker.setAttribute('aria-label', marker.title);
            marker.textContent = '●';
            marker.addEventListener('click', () => this._showDetail(row));
            this.element.append(marker);
        }
    }

    _showDetail(row) {
        this.element.querySelector('.b4a-evidence-map__detail')?.remove();
        const detail = document.createElement('aside');
        detail.className = 'b4a-evidence-map__detail';
        const title = document.createElement('strong');
        title.textContent = '案號：';
        const href = this.options.linkBuilder?.(row);
        if (href) {
            const link = document.createElement('a');
            link.href = href;
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            link.textContent = row.Name || row.Key || '檢視';
            title.append(link);
        } else {
            title.append(document.createTextNode(row.Name || row.Key || '未命名案件'));
        }
        const address = document.createElement('span');
        address.textContent = `地址：${row.Addr?.Address || ''}`;
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'b4a-evidence-map__detail-close';
        close.setAttribute('aria-label', '關閉地圖明細');
        close.textContent = '×';
        close.addEventListener('click', () => detail.remove());
        detail.append(title, address, close);
        this.element.append(detail);
    }

    destroy() {
        this.element.replaceChildren();
        this.element.remove();
    }
}

export default EvidenceMap;
