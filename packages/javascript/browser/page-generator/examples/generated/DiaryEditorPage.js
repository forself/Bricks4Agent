/**
 * DiaryEditorPage - 日記編輯
 *
 * 頁面類型: form
 * 生成時間: 2026-03-11T13:55:17.063Z
 *
 * @module DiaryEditorPage
 */

import { BasePage } from '../../core/BasePage.js';
import { DatePicker } from '../components/DatePicker/DatePicker.js';
import { ColorPicker } from '../components/ColorPicker/ColorPicker.js';
import { GeolocationService } from '../components/services/GeolocationService.js';
import { WeatherService } from '../components/services/WeatherService.js';
import { ToastPanel } from '../components/Panel/ToastPanel.js';
import { ModalPanel } from '../components/Panel/ModalPanel.js';

export class DiaryEditorPage extends BasePage {

    async onInit() {
        this._data = {
            form: {
                  "title": "",
                  "date": "2026-03-11",
                  "mood": "",
                  "content": "",
                  "location": "",
                  "weather": "",
                  "backgroundColor": "#ffffff",
                  "isPrivate": false
        },
            loading: false,
            submitting: false,
            error: null
        };

        // 初始化地理位置服務
        this._geoService = new GeolocationService();

        // 初始化天氣服務
        this._weatherService = new WeatherService();

        // 執行初始化行為
        await this._loadDiaryIfEditing();
    }

    template() {
        const { form, loading, submitting, error } = this._data;

        return `
            <div class="diary-editor-page">
                <header class="page-header">
                    <h1>日記編輯</h1>
                </header>

                ${error ? `
                    <div class="alert alert-error">
                        <p>${this.esc(error)}</p>
                    </div>
                ` : ''}

                <form id="main-form" class="form-container">
                    
                            <div class="form-group">
                                <label for="title">標題 *</label>
                                <input type="text" id="title" name="title"
                                       value="${this.escAttr(this._data.form.title)}"
                                       required
                                       maxlength="100">
                                
                            </div>
                    
                            <div class="form-group">
                                <label for="date">日期 *</label>
                                <div id="date-picker"></div>
                            </div>
                    
                            <div class="form-group">
                                <label for="mood">心情</label>
                                <select id="mood" name="mood" >
                                    <option value="">請選擇</option>
                                    
                                        <option value="happy" ${this._data.form.mood === 'happy' ? 'selected' : ''}>開心 😊</option>
                                    
                                        <option value="neutral" ${this._data.form.mood === 'neutral' ? 'selected' : ''}>普通 😐</option>
                                    
                                        <option value="sad" ${this._data.form.mood === 'sad' ? 'selected' : ''}>難過 😢</option>
                                    
                                        <option value="angry" ${this._data.form.mood === 'angry' ? 'selected' : ''}>生氣 😠</option>
                                    
                                        <option value="excited" ${this._data.form.mood === 'excited' ? 'selected' : ''}>興奮 🤩</option>
                                    
                                </select>
                            </div>
                    
                            <div class="form-group full-width">
                                <label for="content">內容 *</label>
                                <textarea id="content" name="content"
                                          rows="10"
                                          required>${this.esc(this._data.form.content)}</textarea>
                                
                            </div>
                    
                            <div class="form-group full-width">
                                <label>位置</label>
                                <div class="location-display">
                                    <span id="location-display">${this._data.location?.address?.shortName || '尚未定位'}</span>
                                    <button type="button" class="btn btn-secondary btn-sm" data-action="get-location" data-field="location">
                                        取得位置
                                    </button>
                                </div>
                            </div>
                    
                            <div class="form-group full-width">
                                <label>天氣</label>
                                <div class="weather-display" id="weather-display">
                                    ${this._data.weather ? `
                                        <span class="weather-icon">${this._data.weather.icon}</span>
                                        <span class="weather-temp">${this._data.weather.temperature}${this._data.weather.unit}</span>
                                        <span class="weather-desc">${this._data.weather.description}</span>
                                    ` : '需要先取得位置'}
                                </div>
                            </div>
                    
                            <div class="form-group">
                                <label for="backgroundColor">背景顏色</label>
                                <div id="backgroundColor-picker"></div>
                            </div>
                    
                            <div class="form-group">
                                <label class="toggle-label">
                                    <span>設為私密</span>
                                    <input type="checkbox" id="isPrivate" name="isPrivate"
                                           class="toggle-input"
                                           ${this._data.form.isPrivate ? 'checked' : ''}>
                                    <span class="toggle-switch"></span>
                                </label>
                            </div>

                    <div class="form-actions">
                        <button type="submit" class="btn btn-primary" ${submitting ? 'disabled' : ''}>
                            ${submitting ? '處理中...' : '儲存'}
                        </button>
                    </div>
                </form>
            </div>
        `;
    }

    events() {
        return {
                  "submit #main-form": "onSubmit",
                  "input .form-group input": "onInput",
                  "input .form-group textarea": "onInput",
                  "change .form-group select": "onInput",
                  "click [data-action=\"get-location\"][data-field=\"location\"]": "onGetLocation_location",
                  "change #location": "onFieldChange_location"
        };
    }

    async onMounted() {

        // 初始化日期選擇器: date
        this._datePicker = new DatePicker(this.$('#date-picker'), {
            value: this._data.form.date,
            onChange: (date) => {
                this._data.form.date = date;
            }
        });

        // 初始化顏色選擇器: backgroundColor
        this._backgroundColorPicker = new ColorPicker(this.$('#backgroundColor-picker'), {
            value: this._data.form.backgroundColor,
            onChange: (color) => {
                this._data.form.backgroundColor = color;
            }
        });
    }

    onInput(event) {
        const { name, value, type, checked } = event.target;
        this._data.form[name] = type === 'checkbox' ? checked : value;
    }

    async onSubmit(event) {
        event.preventDefault();

        // 驗證
        if (!this._validate()) {
            return;
        }

        this._data.submitting = true;
        this._data.error = null;
        this._scheduleUpdate();

        try {
            await this._save();
            this.showMessage('儲存成功!', 'success');

            // 執行儲存後行為
            await this._navigateToList();

        } catch (error) {
            this._data.error = error.message || '操作失敗';
            this.showMessage('操作失敗', 'error');
        } finally {
            this._data.submitting = false;
            this._scheduleUpdate();
        }
    }

    _validate() {
        const { form } = this._data;

        if (!form.title) {
            this._data.error = '請填寫標題';
            this._scheduleUpdate();
            return false;
        }

        if (!form.date) {
            this._data.error = '請填寫日期';
            this._scheduleUpdate();
            return false;
        }

        if (!form.content) {
            this._data.error = '請填寫內容';
            this._scheduleUpdate();
            return false;
        }

        return true;
    }

    async onGetLocation_location() {
        try {
            this.$('#location-display').textContent = '定位中...';
            const location = await this._geoService.getLocationInfo();
            this._data.location = location;
            this._data.form.location = `${location.latitude},${location.longitude}`;
            this._scheduleUpdate();

            // 自動取得天氣
            await this._getWeather_weather(location.latitude, location.longitude);

        } catch (error) {
            this.showMessage(error.message || '定位失敗', 'error');
        }
    }

    async _getWeather_weather(latitude, longitude) {
        try {
            const weather = await this._weatherService.getCurrentWeather(latitude, longitude);
            this._data.weather = weather;
            this._scheduleUpdate();
        } catch (error) {
            console.error('[天氣服務] 取得失敗:', error);
        }
    }

    async _save() {
        const data = { ...this._data.form };

        // 判斷是新增還是更新
        const isNew = !this.params.id;
        const endpoint = isNew ? '/api/diary' : `/api/diary/${this.params.id}`;
        const method = isNew ? 'post' : 'put';

        const response = await this.api[method](endpoint, data);
        return response;
    }

    async _loadData() {
        if (!this.params.id) return;

        this._data.loading = true;
        this._scheduleUpdate();

        try {
            const response = await this.api.get(`/api/diary/${this.params.id}`);
            this._data.form = response.data;
        } catch (error) {
            this._data.error = '載入資料失敗';
        } finally {
            this._data.loading = false;
            this._scheduleUpdate();
        }
    }

    async _delete() {
        if (!this.params.id) return;

        const confirmed = await this.confirm({
            title: '確認刪除',
            message: '確定要刪除此項目嗎？此操作無法復原。'
        });

        if (!confirmed) return;

        try {
            await this.api.delete(`/api/diary/${this.params.id}`);
            this.showMessage('刪除成功', 'success');

            this.navigate(-1);

        } catch (error) {
            this.showMessage('刪除失敗', 'error');
        }
    }

    /**
     * 初始化行為
     * TODO: 實作此方法
     */
    async _loadDiaryIfEditing() {
        // TODO: 請實作此方法
        console.log('[DiaryEditorPage] _loadDiaryIfEditing 尚未實作');
    }

    /**
     * 儲存後行為
     * TODO: 實作此方法
     */
    async _navigateToList() {
        // TODO: 請實作此方法
        console.log('[DiaryEditorPage] _navigateToList 尚未實作');
    }

    /**
     * 欄位 location 的觸發器
     * TODO: 實作此方法
     */
    async _fetchWeatherOnLocationChange() {
        // TODO: 請實作此方法
        console.log('[DiaryEditorPage] _fetchWeatherOnLocationChange 尚未實作');
    }
}

export default DiaryEditorPage;
