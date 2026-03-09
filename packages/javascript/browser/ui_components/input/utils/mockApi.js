/**
 * 模擬 API 服務
 * 用於模擬從後端取得資料
 */

const DELAY = 300; // 模擬網路延遲

// 模擬縣市資料（六都 + 其餘直轄市/縣市）
const TAIWAN_DATA = {
    'Taipei': { name: '台北市', districts: ['中正區', '大同區', '中山區', '松山區', '大安區', '萬華區', '信義區', '士林區', '北投區', '內湖區', '南港區', '文山區'] },
    'NewTaipei': { name: '新北市', districts: ['板橋區', '三重區', '中和區', '永和區', '新莊區', '新店區', '土城區', '蘆洲區', '樹林區', '汐止區', '鶯歌區', '三峽區', '淡水區', '瑞芳區'] },
    'Taoyuan': { name: '桃園市', districts: ['桃園區', '中壢區', '大溪區', '楊梅區', '蘆竹區', '大園區', '龜山區', '八德區', '龍潭區', '平鎮區', '新屋區', '觀音區'] },
    'Taichung': { name: '台中市', districts: ['中區', '東區', '南區', '西區', '北區', '北屯區', '西屯區', '南屯區', '太平區', '大里區', '霧峰區', '烏日區', '豐原區', '后里區', '神岡區', '大雅區', '潭子區'] },
    'Tainan': { name: '台南市', districts: ['中西區', '東區', '南區', '北區', '安平區', '安南區', '永康區', '歸仁區', '新化區', '左鎮區', '玉井區', '楠西區', '仁德區', '關廟區'] },
    'Kaohsiung': { name: '高雄市', districts: ['楠梓區', '左營區', '鼓山區', '三民區', '鹽埕區', '前金區', '新興區', '苓雅區', '前鎮區', '小港區', '旗津區', '鳳山區', '大寮區', '鳥松區', '仁武區'] },
    'Keelung': { name: '基隆市', districts: ['中正區', '七堵區', '暖暖區', '仁愛區', '中山區', '安樂區', '信義區'] },
    'Hsinchu': { name: '新竹市', districts: ['東區', '北區', '香山區'] },
    'HsinchuCounty': { name: '新竹縣', districts: ['竹北市', '竹東鎮', '新埔鎮', '關西鎮', '湖口鄉', '新豐鄉', '芎林鄉', '橫山鄉', '北埔鄉', '寶山鄉'] },
    'Miaoli': { name: '苗栗縣', districts: ['苗栗市', '頭份市', '竹南鎮', '後龍鎮', '通霄鎮', '苑裡鎮', '卓蘭鎮', '造橋鄉', '西湖鄉'] },
    'Changhua': { name: '彰化縣', districts: ['彰化市', '員林市', '鹿港鎮', '和美鎮', '北斗鎮', '溪湖鎮', '田中鎮', '二林鎮', '花壇鄉', '芬園鄉'] },
    'Nantou': { name: '南投縣', districts: ['南投市', '埔里鎮', '草屯鎮', '竹山鎮', '集集鎮', '名間鄉', '鹿谷鄉', '中寮鄉', '魚池鄉', '水里鄉'] },
    'Yunlin': { name: '雲林縣', districts: ['斗六市', '虎尾鎮', '斗南鎮', '西螺鎮', '土庫鎮', '北港鎮', '古坑鄉', '大埤鄉', '莿桐鄉', '林內鄉'] },
    'Chiayi': { name: '嘉義市', districts: ['東區', '西區'] },
    'ChiayiCounty': { name: '嘉義縣', districts: ['太保市', '朴子市', '布袋鎮', '大林鎮', '民雄鄉', '溪口鄉', '新港鄉', '六腳鄉', '東石鄉', '竹崎鄉'] },
    'Pingtung': { name: '屏東縣', districts: ['屏東市', '潮州鎮', '東港鎮', '恆春鎮', '萬丹鄉', '長治鄉', '麟洛鄉', '九如鄉', '里港鄉', '鹽埔鄉'] },
    'Yilan': { name: '宜蘭縣', districts: ['宜蘭市', '羅東鎮', '蘇澳鎮', '頭城鎮', '礁溪鄉', '壯圍鄉', '員山鄉', '冬山鄉', '五結鄉', '三星鄉'] },
    'Hualien': { name: '花蓮縣', districts: ['花蓮市', '鳳林鎮', '玉里鎮', '新城鄉', '吉安鄉', '壽豐鄉', '光復鄉', '豐濱鄉', '瑞穗鄉', '富里鄉'] },
    'Taitung': { name: '台東縣', districts: ['台東市', '成功鎮', '關山鎮', '卑南鄉', '太麻里鄉', '大武鄉', '綠島鄉', '蘭嶼鄉', '池上鄉', '東河鄉'] },
    'Penghu': { name: '澎湖縣', districts: ['馬公市', '湖西鄉', '白沙鄉', '西嶼鄉', '望安鄉', '七美鄉'] },
    'Kinmen': { name: '金門縣', districts: ['金城鎮', '金湖鎮', '金沙鎮', '金寧鄉', '烈嶼鄉', '烏坵鄉'] },
    'Lienchiang': { name: '連江縣', districts: ['南竿鄉', '北竿鄉', '莒光鄉', '東引鄉'] }
};

// 模擬單位層級資料
const ORGANIZATION_DATA = {
    'root': [
        { id: '100', name: '總公司' },
        { id: '200', name: '分公司' }
    ],
    '100': [
        { id: '110', name: '行政處' },
        { id: '120', name: '業務處' },
        { id: '130', name: '資訊處' }
    ],
    '110': [
        { id: '111', name: '人資部' },
        { id: '112', name: '總務部' }
    ],
    '120': [
        { id: '121', name: '國內業務部' },
        { id: '122', name: '海外業務部' }
    ],
    '130': [
        { id: '131', name: '系統開發部' },
        { id: '132', name: '網路管理部' },
        { id: '133', name: '資安部' }
    ],
    '131': [
        { id: '1311', name: '前端組' },
        { id: '1312', name: '後端組' }
    ],
    '200': [
        { id: '210', name: '台中辦事處' },
        { id: '220', name: '高雄辦事處' }
    ]
};

export const mockApi = {
    /**
     * 取得縣市列表
     */
    async getCities() {
        return new Promise(resolve => {
            setTimeout(() => {
                const cities = Object.entries(TAIWAN_DATA).map(([key, value]) => ({
                    value: key,
                    label: value.name
                }));
                resolve(cities);
            }, DELAY);
        });
    },

    /**
     * 取得行政區列表
     * @param {string} cityKey 縣市代碼
     */
    async getDistricts(cityKey) {
        return new Promise(resolve => {
            setTimeout(() => {
                const city = TAIWAN_DATA[cityKey];
                resolve(city ? city.districts : []);
            }, DELAY);
        });
    },

    /**
     * 取得單位列表
     * @param {string} parentId 父單位 ID
     */
    async getUnitList(parentId) {
        return new Promise(resolve => {
            setTimeout(() => {
                // 如果是空字串，回傳第一層
                const key = parentId || 'root';
                resolve(ORGANIZATION_DATA[key] || []);
            }, DELAY);
        });
    }
};
