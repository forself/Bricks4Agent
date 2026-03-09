# 工具服務 (Utility Services)

此目錄包含可重用的工具服務類別，用於處理常見的 Web 應用功能需求。

## 服務列表

| 服務 | 說明 | 依賴 |
|------|------|------|
| `GeolocationService` | 地理位置服務 | Nominatim API |
| `WeatherService` | 天氣資訊服務 | Open-Meteo API |
| `security.js` | 安全工具函數 | 無 |
| `SimpleZip.js` | ZIP 壓縮工具 | 無 |

---

## GeolocationService

地理位置服務 - 取得使用者位置並進行反向地理編碼。

### Features

- **取得當前位置**：使用 Browser Geolocation API
- **反向地理編碼**：座標轉地址 (使用 Nominatim OpenStreetMap)
- **位置監聽**：監聽位置變化
- **距離計算**：計算兩點距離 (Haversine 公式)
- **多語言支援**：支援繁體中文、英文

### Usage

```javascript
import { GeolocationService, GeolocationError } from './GeolocationService.js';

// 建立服務實例
const geoService = new GeolocationService({
    language: 'zh-TW',      // 反向地理編碼語言
    timeout: 10000,         // 定位逾時 (ms)
    enableHighAccuracy: true
});

// 取得位置資訊（含地址）
try {
    const location = await geoService.getLocationInfo();
    console.log(`位置: ${location.address.shortName}`);
    console.log(`座標: ${location.latitude}, ${location.longitude}`);
} catch (error) {
    if (error instanceof GeolocationError) {
        console.error(`定位錯誤 (${error.code}): ${error.message}`);
    }
}

// 僅取得座標
const position = await geoService.getCurrentPosition();

// 反向地理編碼
const address = await geoService.reverseGeocode(25.0330, 121.5654);
console.log(address.shortName);  // "台北市信義區"

// 監聽位置變化
const watchId = geoService.watchPosition(
    (position) => console.log('位置更新:', position),
    (error) => console.error('錯誤:', error)
);

// 停止監聽
geoService.clearWatch(watchId);

// 計算兩點距離
const distance = GeolocationService.calculateDistance(
    25.0330, 121.5654,  // 台北101
    24.1477, 120.6736   // 台中火車站
);
console.log(GeolocationService.formatDistance(distance));  // "162.3 公里"
```

### 錯誤代碼

| Code | 說明 |
|------|------|
| `NOT_SUPPORTED` | 瀏覽器不支援 Geolocation |
| `PERMISSION_DENIED` | 位置權限被拒絕 |
| `POSITION_UNAVAILABLE` | 無法取得位置 |
| `TIMEOUT` | 定位逾時 |

---

## WeatherService

天氣服務 - 使用 Open-Meteo API 取得天氣資訊。

### Features

- **當前天氣**：溫度、體感溫度、濕度、風速
- **天氣預報**：每日預報 (最多 16 天)
- **每小時預報**：每小時預報 (最多 384 小時)
- **WMO 代碼解析**：天氣代碼轉描述文字與圖示
- **多語言支援**：繁體中文、英文
- **免費無 API Key**：使用 Open-Meteo 公開 API

### Usage

```javascript
import { WeatherService, WeatherError } from './WeatherService.js';

// 建立服務實例
const weatherService = new WeatherService({
    language: 'zh-TW',           // 語言
    temperatureUnit: 'celsius'   // 溫度單位 (celsius/fahrenheit)
});

// 取得當前天氣
try {
    const weather = await weatherService.getCurrentWeather(25.0330, 121.5654);
    console.log(`${weather.icon} ${weather.temperature}${weather.unit}`);
    console.log(`天氣: ${weather.description}`);
    console.log(`體感溫度: ${weather.feelsLike}${weather.unit}`);
    console.log(`濕度: ${weather.humidity}%`);
    console.log(`風速: ${weather.windSpeed} km/h (${weather.windDirectionText})`);
} catch (error) {
    console.error('取得天氣失敗:', error.message);
}

// 取得 7 天預報
const forecast = await weatherService.getForecast(25.0330, 121.5654, 7);
forecast.forEach(day => {
    console.log(`${day.date}: ${day.icon} ${day.tempMin}~${day.tempMax}${day.unit}`);
});

// 取得每小時預報
const hourly = await weatherService.getHourlyForecast(25.0330, 121.5654, 24);
hourly.forEach(hour => {
    console.log(`${hour.time}: ${hour.icon} ${hour.temperature}${hour.unit}`);
});

// 取得天氣圖示
const icon = WeatherService.getWeatherIcon(0);  // ☀️

// 取得天氣類型
const type = WeatherService.getWeatherType(61);  // 'rain'

// 格式化溫度
const temp = WeatherService.formatTemperature(25, 'celsius');  // "25°C"
```

### WMO 天氣代碼

| 代碼 | 描述 (中文) | 描述 (英文) | 圖示 |
|------|-------------|-------------|------|
| 0 | 晴朗 | Clear sky | ☀️ |
| 1 | 大致晴朗 | Mainly clear | ☀️ |
| 2 | 局部多雲 | Partly cloudy | ☁️ |
| 3 | 多雲 | Overcast | ☁️ |
| 45-48 | 有霧/霧凇 | Fog | 🌫️ |
| 51-57 | 毛毛雨 | Drizzle | 🌧️ |
| 61-67 | 雨 | Rain | 🌧️ |
| 71-77 | 雪 | Snow | ❄️ |
| 80-82 | 陣雨 | Rain showers | 🌧️ |
| 85-86 | 陣雪 | Snow showers | ❄️ |
| 95-99 | 雷雨 | Thunderstorm | ⛈️ |

---

## 使用範例：位置 + 天氣

```javascript
import { GeolocationService } from './GeolocationService.js';
import { WeatherService } from './WeatherService.js';

async function getLocationWeather() {
    const geoService = new GeolocationService({ language: 'zh-TW' });
    const weatherService = new WeatherService({ language: 'zh-TW' });

    try {
        // 取得位置
        const location = await geoService.getLocationInfo();
        console.log(`📍 ${location.address.shortName}`);

        // 取得天氣
        const weather = await weatherService.getCurrentWeather(
            location.latitude,
            location.longitude
        );
        console.log(`${weather.icon} ${weather.temperature}${weather.unit} ${weather.description}`);

    } catch (error) {
        console.error('錯誤:', error.message);
    }
}

getLocationWeather();
```

---

## 瀏覽器支援

- Chrome/Edge 90+
- Firefox 88+
- Safari 14+

## API 參考

- [Nominatim OpenStreetMap](https://nominatim.org/release-docs/latest/api/Reverse/)
- [Open-Meteo](https://open-meteo.com/en/docs)

---

**Created for Bricks4Agent Project**
