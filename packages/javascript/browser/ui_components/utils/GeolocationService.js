/**
 * GeolocationService
 * 地理位置服務 - 取得使用者位置並進行反向地理編碼
 *
 * 功能：
 * - 取得當前位置 (經緯度)
 * - 反向地理編碼 (座標轉地址)
 * - 計算兩點距離
 * - 錯誤處理與多語言訊息
 *
 * 使用的 API：
 * - Browser Geolocation API
 * - Nominatim OpenStreetMap (反向地理編碼)
 *
 * @module GeolocationService
 */

export class GeolocationService {
    /**
     * @param {Object} options
     * @param {string} options.language - 反向地理編碼語言 (預設 zh-TW)
     * @param {number} options.timeout - 定位逾時毫秒 (預設 10000)
     * @param {boolean} options.enableHighAccuracy - 啟用高精度 (預設 true)
     * @param {number} options.maximumAge - 快取時間毫秒 (預設 0)
     */
    constructor(options = {}) {
        this.language = options.language || 'zh-TW';
        this.timeout = options.timeout || 10000;
        this.enableHighAccuracy = options.enableHighAccuracy !== false;
        this.maximumAge = options.maximumAge || 0;
    }

    /**
     * 檢查瀏覽器是否支援 Geolocation
     * @returns {boolean}
     */
    static isSupported() {
        return 'geolocation' in navigator;
    }

    /**
     * 取得當前位置
     * @returns {Promise<GeolocationPosition>}
     */
    getCurrentPosition() {
        return new Promise((resolve, reject) => {
            if (!GeolocationService.isSupported()) {
                reject(new GeolocationError('NOT_SUPPORTED', '您的瀏覽器不支援定位功能'));
                return;
            }

            navigator.geolocation.getCurrentPosition(
                resolve,
                (error) => {
                    reject(this._handleError(error));
                },
                {
                    enableHighAccuracy: this.enableHighAccuracy,
                    timeout: this.timeout,
                    maximumAge: this.maximumAge
                }
            );
        });
    }

    /**
     * 取得位置資訊（含地址）
     * @returns {Promise<LocationInfo>}
     */
    async getLocationInfo() {
        const position = await this.getCurrentPosition();
        const { latitude, longitude, accuracy } = position.coords;

        // 反向地理編碼取得地址
        const address = await this.reverseGeocode(latitude, longitude);

        return {
            latitude,
            longitude,
            accuracy,
            address,
            timestamp: position.timestamp
        };
    }

    /**
     * 反向地理編碼（座標轉地址）
     * @param {number} latitude - 緯度
     * @param {number} longitude - 經度
     * @returns {Promise<AddressInfo>}
     */
    async reverseGeocode(latitude, longitude) {
        try {
            const url = `https://nominatim.openstreetmap.org/reverse?format=json&lat=${latitude}&lon=${longitude}&accept-language=${this.language}`;

            const response = await fetch(url, {
                headers: {
                    'User-Agent': 'Bricks4Agent/1.0'
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const data = await response.json();

            if (!data.display_name) {
                return {
                    displayName: `${latitude.toFixed(4)}, ${longitude.toFixed(4)}`,
                    shortName: `${latitude.toFixed(4)}, ${longitude.toFixed(4)}`,
                    raw: null
                };
            }

            // 解析地址
            const address = data.address || {};
            const parts = [];

            // 組合簡短地址
            if (address.city || address.town || address.village) {
                parts.push(address.city || address.town || address.village);
            }
            if (address.suburb || address.district) {
                parts.push(address.suburb || address.district);
            }

            const shortName = parts.join('') || data.display_name.split(',')[0];

            return {
                displayName: data.display_name,
                shortName,
                country: address.country,
                state: address.state,
                city: address.city || address.town || address.village,
                district: address.suburb || address.district,
                road: address.road,
                raw: address
            };
        } catch (error) {
            console.error('[GeolocationService] 反向地理編碼失敗:', error);
            return {
                displayName: `${latitude.toFixed(4)}, ${longitude.toFixed(4)}`,
                shortName: `${latitude.toFixed(4)}, ${longitude.toFixed(4)}`,
                raw: null
            };
        }
    }

    /**
     * 監聽位置變化
     * @param {Function} successCallback - 成功回調 (position)
     * @param {Function} errorCallback - 錯誤回調 (error)
     * @returns {number} watchId - 可用於 clearWatch
     */
    watchPosition(successCallback, errorCallback) {
        if (!GeolocationService.isSupported()) {
            if (errorCallback) {
                errorCallback(new GeolocationError('NOT_SUPPORTED', '您的瀏覽器不支援定位功能'));
            }
            return -1;
        }

        return navigator.geolocation.watchPosition(
            successCallback,
            (error) => {
                if (errorCallback) {
                    errorCallback(this._handleError(error));
                }
            },
            {
                enableHighAccuracy: this.enableHighAccuracy,
                timeout: this.timeout,
                maximumAge: this.maximumAge
            }
        );
    }

    /**
     * 停止監聽位置變化
     * @param {number} watchId
     */
    clearWatch(watchId) {
        if (watchId >= 0 && GeolocationService.isSupported()) {
            navigator.geolocation.clearWatch(watchId);
        }
    }

    /**
     * 計算兩點之間的距離（公里）
     * 使用 Haversine 公式
     * @param {number} lat1 - 點1緯度
     * @param {number} lon1 - 點1經度
     * @param {number} lat2 - 點2緯度
     * @param {number} lon2 - 點2經度
     * @returns {number} 距離（公里）
     */
    static calculateDistance(lat1, lon1, lat2, lon2) {
        const R = 6371; // 地球半徑（公里）
        const dLat = GeolocationService._toRad(lat2 - lat1);
        const dLon = GeolocationService._toRad(lon2 - lon1);

        const a =
            Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(GeolocationService._toRad(lat1)) *
            Math.cos(GeolocationService._toRad(lat2)) *
            Math.sin(dLon / 2) *
            Math.sin(dLon / 2);

        const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
        return R * c;
    }

    /**
     * 格式化距離顯示
     * @param {number} distanceKm - 距離（公里）
     * @returns {string}
     */
    static formatDistance(distanceKm) {
        if (distanceKm < 1) {
            return `${Math.round(distanceKm * 1000)} 公尺`;
        }
        return `${distanceKm.toFixed(1)} 公里`;
    }

    /**
     * 角度轉弧度
     * @private
     */
    static _toRad(deg) {
        return deg * (Math.PI / 180);
    }

    /**
     * 處理 Geolocation 錯誤
     * @private
     */
    _handleError(error) {
        const errorMessages = {
            1: { code: 'PERMISSION_DENIED', message: '位置權限被拒絕' },
            2: { code: 'POSITION_UNAVAILABLE', message: '無法取得位置資訊' },
            3: { code: 'TIMEOUT', message: '取得位置逾時' }
        };

        const errorInfo = errorMessages[error.code] || {
            code: 'UNKNOWN',
            message: '無法取得位置'
        };

        return new GeolocationError(errorInfo.code, errorInfo.message);
    }
}

/**
 * 地理位置錯誤類別
 */
export class GeolocationError extends Error {
    constructor(code, message) {
        super(message);
        this.name = 'GeolocationError';
        this.code = code;
    }
}

/**
 * @typedef {Object} LocationInfo
 * @property {number} latitude - 緯度
 * @property {number} longitude - 經度
 * @property {number} accuracy - 精確度（公尺）
 * @property {AddressInfo} address - 地址資訊
 * @property {number} timestamp - 時間戳記
 */

/**
 * @typedef {Object} AddressInfo
 * @property {string} displayName - 完整地址
 * @property {string} shortName - 簡短地址
 * @property {string} country - 國家
 * @property {string} state - 州/省
 * @property {string} city - 城市
 * @property {string} district - 區域
 * @property {string} road - 道路
 * @property {Object} raw - 原始回應資料
 */

export default GeolocationService;
