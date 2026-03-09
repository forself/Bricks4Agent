/**
 * WeatherService
 * 天氣服務 - 使用 Open-Meteo API 取得天氣資訊
 *
 * 功能：
 * - 取得當前天氣
 * - 取得天氣預報
 * - WMO 天氣代碼解析
 * - 多語言支援 (繁體中文、英文)
 *
 * 使用的 API：
 * - Open-Meteo (免費、無需 API Key)
 *
 * @module WeatherService
 */

export class WeatherService {
    /**
     * @param {Object} options
     * @param {string} options.language - 語言 (zh-TW, en)
     * @param {string} options.temperatureUnit - 溫度單位 (celsius, fahrenheit)
     */
    constructor(options = {}) {
        this.language = options.language || 'zh-TW';
        this.temperatureUnit = options.temperatureUnit || 'celsius';
        this._baseUrl = 'https://api.open-meteo.com/v1/forecast';
    }

    /**
     * 取得當前天氣
     * @param {number} latitude - 緯度
     * @param {number} longitude - 經度
     * @returns {Promise<CurrentWeather>}
     */
    async getCurrentWeather(latitude, longitude) {
        const params = new URLSearchParams({
            latitude: latitude.toString(),
            longitude: longitude.toString(),
            current: 'temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m',
            temperature_unit: this.temperatureUnit
        });

        const response = await fetch(`${this._baseUrl}?${params}`);

        if (!response.ok) {
            throw new WeatherError('API_ERROR', `HTTP ${response.status}`);
        }

        const data = await response.json();

        if (!data.current) {
            throw new WeatherError('NO_DATA', '無法取得天氣資料');
        }

        const current = data.current;
        const weatherCode = current.weather_code;

        return {
            temperature: current.temperature_2m,
            feelsLike: current.apparent_temperature,
            humidity: current.relative_humidity_2m,
            weatherCode,
            description: this.getWeatherDescription(weatherCode),
            icon: WeatherService.getWeatherIcon(weatherCode),
            windSpeed: current.wind_speed_10m,
            windDirection: current.wind_direction_10m,
            windDirectionText: WeatherService.getWindDirection(current.wind_direction_10m),
            timestamp: new Date(current.time).getTime(),
            unit: this.temperatureUnit === 'celsius' ? '°C' : '°F'
        };
    }

    /**
     * 取得天氣預報
     * @param {number} latitude - 緯度
     * @param {number} longitude - 經度
     * @param {number} days - 預報天數 (1-16)
     * @returns {Promise<DailyForecast[]>}
     */
    async getForecast(latitude, longitude, days = 7) {
        const params = new URLSearchParams({
            latitude: latitude.toString(),
            longitude: longitude.toString(),
            daily: 'weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max',
            temperature_unit: this.temperatureUnit,
            forecast_days: Math.min(16, Math.max(1, days)).toString()
        });

        const response = await fetch(`${this._baseUrl}?${params}`);

        if (!response.ok) {
            throw new WeatherError('API_ERROR', `HTTP ${response.status}`);
        }

        const data = await response.json();

        if (!data.daily) {
            throw new WeatherError('NO_DATA', '無法取得天氣預報');
        }

        const daily = data.daily;
        const forecasts = [];

        for (let i = 0; i < daily.time.length; i++) {
            const weatherCode = daily.weather_code[i];
            forecasts.push({
                date: daily.time[i],
                weatherCode,
                description: this.getWeatherDescription(weatherCode),
                icon: WeatherService.getWeatherIcon(weatherCode),
                tempMax: daily.temperature_2m_max[i],
                tempMin: daily.temperature_2m_min[i],
                precipitation: daily.precipitation_sum[i],
                precipitationProbability: daily.precipitation_probability_max[i],
                unit: this.temperatureUnit === 'celsius' ? '°C' : '°F'
            });
        }

        return forecasts;
    }

    /**
     * 取得每小時預報
     * @param {number} latitude - 緯度
     * @param {number} longitude - 經度
     * @param {number} hours - 預報小時數 (最多 384)
     * @returns {Promise<HourlyForecast[]>}
     */
    async getHourlyForecast(latitude, longitude, hours = 24) {
        const params = new URLSearchParams({
            latitude: latitude.toString(),
            longitude: longitude.toString(),
            hourly: 'temperature_2m,weather_code,precipitation_probability',
            temperature_unit: this.temperatureUnit,
            forecast_hours: Math.min(384, Math.max(1, hours)).toString()
        });

        const response = await fetch(`${this._baseUrl}?${params}`);

        if (!response.ok) {
            throw new WeatherError('API_ERROR', `HTTP ${response.status}`);
        }

        const data = await response.json();

        if (!data.hourly) {
            throw new WeatherError('NO_DATA', '無法取得每小時預報');
        }

        const hourly = data.hourly;
        const forecasts = [];

        for (let i = 0; i < Math.min(hours, hourly.time.length); i++) {
            const weatherCode = hourly.weather_code[i];
            forecasts.push({
                time: hourly.time[i],
                temperature: hourly.temperature_2m[i],
                weatherCode,
                description: this.getWeatherDescription(weatherCode),
                icon: WeatherService.getWeatherIcon(weatherCode),
                precipitationProbability: hourly.precipitation_probability[i],
                unit: this.temperatureUnit === 'celsius' ? '°C' : '°F'
            });
        }

        return forecasts;
    }

    /**
     * 根據 WMO 天氣代碼取得天氣描述
     * @param {number} code - WMO 天氣代碼
     * @returns {string}
     */
    getWeatherDescription(code) {
        const descriptions = this.language === 'zh-TW'
            ? WMO_CODES_ZH_TW
            : WMO_CODES_EN;
        return descriptions[code] || (this.language === 'zh-TW' ? '未知' : 'Unknown');
    }

    /**
     * 根據 WMO 天氣代碼取得天氣圖示
     * @param {number} code - WMO 天氣代碼
     * @returns {string} emoji 圖示
     */
    static getWeatherIcon(code) {
        if (code === null || code === undefined) return '?';
        if (code === 0 || code === 1) return '☀️';
        if (code === 2 || code === 3) return '☁️';
        if (code >= 45 && code <= 48) return '🌫️';
        if ((code >= 51 && code <= 57) || (code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return '🌧️';
        if ((code >= 71 && code <= 77) || (code >= 85 && code <= 86)) return '❄️';
        if (code >= 95) return '⛈️';
        return '🌤️';
    }

    /**
     * 取得天氣類型
     * @param {number} code - WMO 天氣代碼
     * @returns {string}
     */
    static getWeatherType(code) {
        if (code === 0 || code === 1) return 'clear';
        if (code === 2 || code === 3) return 'cloudy';
        if (code >= 45 && code <= 48) return 'fog';
        if ((code >= 51 && code <= 57) || (code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return 'rain';
        if ((code >= 71 && code <= 77) || (code >= 85 && code <= 86)) return 'snow';
        if (code >= 95) return 'thunderstorm';
        return 'unknown';
    }

    /**
     * 取得風向文字
     * @param {number} degrees - 風向角度 (0-360)
     * @returns {string}
     */
    static getWindDirection(degrees) {
        const directions = ['北', '東北', '東', '東南', '南', '西南', '西', '西北'];
        const index = Math.round(degrees / 45) % 8;
        return directions[index];
    }

    /**
     * 格式化溫度
     * @param {number} temp - 溫度值
     * @param {string} unit - 單位 (celsius/fahrenheit)
     * @returns {string}
     */
    static formatTemperature(temp, unit = 'celsius') {
        const symbol = unit === 'celsius' ? '°C' : '°F';
        return `${Math.round(temp)}${symbol}`;
    }
}

/**
 * 天氣錯誤類別
 */
export class WeatherError extends Error {
    constructor(code, message) {
        super(message);
        this.name = 'WeatherError';
        this.code = code;
    }
}

/**
 * WMO 天氣代碼 - 繁體中文
 */
const WMO_CODES_ZH_TW = {
    0: '晴朗',
    1: '大致晴朗',
    2: '局部多雲',
    3: '多雲',
    45: '有霧',
    48: '霧凇',
    51: '輕微毛毛雨',
    53: '中等毛毛雨',
    55: '濃密毛毛雨',
    56: '輕微凍毛毛雨',
    57: '濃密凍毛毛雨',
    61: '小雨',
    63: '中雨',
    65: '大雨',
    66: '輕微凍雨',
    67: '大凍雨',
    71: '小雪',
    73: '中雪',
    75: '大雪',
    77: '雪粒',
    80: '小陣雨',
    81: '中陣雨',
    82: '大陣雨',
    85: '小陣雪',
    86: '大陣雪',
    95: '雷雨',
    96: '雷雨伴小冰雹',
    99: '雷雨伴大冰雹'
};

/**
 * WMO 天氣代碼 - 英文
 */
const WMO_CODES_EN = {
    0: 'Clear sky',
    1: 'Mainly clear',
    2: 'Partly cloudy',
    3: 'Overcast',
    45: 'Fog',
    48: 'Depositing rime fog',
    51: 'Light drizzle',
    53: 'Moderate drizzle',
    55: 'Dense drizzle',
    56: 'Light freezing drizzle',
    57: 'Dense freezing drizzle',
    61: 'Slight rain',
    63: 'Moderate rain',
    65: 'Heavy rain',
    66: 'Light freezing rain',
    67: 'Heavy freezing rain',
    71: 'Slight snow fall',
    73: 'Moderate snow fall',
    75: 'Heavy snow fall',
    77: 'Snow grains',
    80: 'Slight rain showers',
    81: 'Moderate rain showers',
    82: 'Violent rain showers',
    85: 'Slight snow showers',
    86: 'Heavy snow showers',
    95: 'Thunderstorm',
    96: 'Thunderstorm with slight hail',
    99: 'Thunderstorm with heavy hail'
};

/**
 * @typedef {Object} CurrentWeather
 * @property {number} temperature - 溫度
 * @property {number} feelsLike - 體感溫度
 * @property {number} humidity - 相對濕度 (%)
 * @property {number} weatherCode - WMO 天氣代碼
 * @property {string} description - 天氣描述
 * @property {string} icon - 天氣圖示 (emoji)
 * @property {number} windSpeed - 風速 (km/h)
 * @property {number} windDirection - 風向 (度)
 * @property {string} windDirectionText - 風向文字
 * @property {number} timestamp - 時間戳記
 * @property {string} unit - 溫度單位
 */

/**
 * @typedef {Object} DailyForecast
 * @property {string} date - 日期 (YYYY-MM-DD)
 * @property {number} weatherCode - WMO 天氣代碼
 * @property {string} description - 天氣描述
 * @property {string} icon - 天氣圖示
 * @property {number} tempMax - 最高溫
 * @property {number} tempMin - 最低溫
 * @property {number} precipitation - 降水量 (mm)
 * @property {number} precipitationProbability - 降水機率 (%)
 * @property {string} unit - 溫度單位
 */

/**
 * @typedef {Object} HourlyForecast
 * @property {string} time - 時間 (ISO 8601)
 * @property {number} temperature - 溫度
 * @property {number} weatherCode - WMO 天氣代碼
 * @property {string} description - 天氣描述
 * @property {string} icon - 天氣圖示
 * @property {number} precipitationProbability - 降水機率 (%)
 * @property {string} unit - 溫度單位
 */

export default WeatherService;
