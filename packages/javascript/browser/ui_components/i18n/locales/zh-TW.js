/**
 * 繁體中文語系包 (zh-TW)
 * 元件庫預設語言
 */
export default {
    // ===== common/ =====

    /** BasicButton — 一般操作按鈕 */
    basicButton: {
        delete: '刪除',
        confirm: '確認',
        yes: '是',
        cancel: '取消',
        no: '否',
        done: '完成',
        close: '關閉',
        search: '搜尋',
        clear: '清除',
        reset: '重設',
        save: '儲存',
        apply: '套用',
        copy: '複製',
        refresh: '刷新',
        addRow: '增加一列',
        selectAll: '全選',
        deselectAll: '取消全選',
        back: '返回',
        next: '下一步',
        prev: '上一步',
        expandAll: '展開全部',
        collapseAll: '收合全部',
        // createDialogButtons 預設值
        confirmLabel: '確認',
        cancelLabel: '取消'
    },

    /** ActionButton — 流程操作按鈕 */
    actionButton: {
        add: '新增',
        delete: '刪除',
        edit: '編輯',
        detail: '詳細',
        submit: '送出',
        reject: '退回',
        archive: '歸檔',
        merge: '整合',
        verify: '檢核',
        withdraw: '撤管',
        report: '陳報',
        transfer: '審轉',
        approve: '審核',
        modify: '修改',
        confirmMessage: '確定要執行此操作嗎？',
        confirmDelete: '確定要刪除嗎？',
        confirmReject: '確定要退回嗎？'
    },

    /** AuthButton — 登入/登出 */
    authButton: {
        login: '登入',
        logout: '登出',
        confirmLogout: '確定要登出嗎？'
    },

    /** EditorButton — 編輯器工具列按鈕 */
    editorButton: {
        bold: '粗體',
        italic: '斜體',
        underline: '底線',
        strikethrough: '刪除線',
        subscript: '下標',
        superscript: '上標',
        heading1: '大標題',
        heading2: '中標題',
        heading3: '小標題',
        paragraph: '段落',
        quote: '引用',
        code: '程式碼',
        alignLeft: '靠左',
        alignCenter: '置中',
        alignRight: '靠右',
        alignJustify: '兩端對齊',
        listBullet: '項目符號',
        listNumber: '編號',
        indent: '增加縮排',
        outdent: '減少縮排',
        undo: '復原',
        redo: '重做',
        link: '連結',
        image: '圖片',
        table: '表格',
        line: '分隔線',
        pageBreak: '分頁',
        pen: '畫筆',
        eraser: '橡皮擦',
        lineTool: '直線',
        highlighter: '螢光筆',
        rect: '矩形',
        circle: '圓形',
        arrow: '箭頭',
        text: '文字',
        select: '選擇',
        measureDistance: '測距',
        measureArea: '測面積',
        coordinate: '座標',
        exportPdf: '匯出 PDF',
        exportWord: '匯出 Word',
        exportMarkdown: '匯出 Markdown',
        exportPng: '匯出 PNG',
        exportJson: '匯出 JSON',
        search: '搜尋',
        replace: '取代',
        fullscreen: '全螢幕',
        clear: '清除',
        clearAll: '清空',
        removeFormat: '清除格式',
        copy: '複製',
        paste: '貼上',
        cut: '剪下',
        toc: '目錄',
        settings: '設定',
        layers: '圖層',
        zoomIn: '放大',
        zoomOut: '縮小',
        insertDrawing: '繪圖',
        insertToc: '目錄',
        header: '頁首',
        footer: '頁尾',
        pageNumber: '頁碼',
        margin: '邊界',
        generateToc: '生成目錄'
    },

    /** Notification — 通知訊息 */
    notification: {
        success: '成功',
        error: '錯誤',
        warning: '警告',
        info: '提示'
    },

    /** SimpleDialog — 對話框 */
    dialog: {
        alert: '提示',
        confirm: '確認',
        confirmBtn: '確定',
        cancelBtn: '取消'
    },

    /** Pagination — 分頁器 */
    pagination: {
        prev: '上一頁',
        next: '下一頁',
        first: '第一頁',
        last: '最後一頁',
        totalPrefix: '共 ',
        totalSuffix: ' 筆',
        perPage: ' 筆/頁',
        goTo: '前往',
        page: '頁',
        jump: '跳轉'
    },

    /** LoadingSpinner — 載入中 */
    loadingSpinner: {
        text: '載入中...'
    },

    /** Breadcrumb — 麵包屑 */
    breadcrumb: {
        home: '首頁'
    },

    /** UploadButton / DownloadButton */
    upload: {
        dragHint: '拖曳檔案至此或點擊上傳',
        uploading: '上傳中 {percent}%',
        success: '上傳成功',
        error: '上傳失敗',
        maxSize: '檔案大小超過限制 ({max})',
        maxCount: '最多上傳 {max} 個檔案',
        uploadLabel: '上傳 {label}',
        uploadAriaLabel: '上傳 {label} 檔案',
        securityPathTraversal: '安全性警告: 檔案名稱 "{name}" 包含無效字元 (路徑遍歷風險)。',
        fileTooLarge: '檔案 "{name}" 超過大小限制 (最大 {max})。',
        formatMismatch: '安全性警告: 檔案 "{name}" 的內容與預期格式不符。\n請確保上傳正確的檔案類型。',
        encodingUtf16LE: '編碼錯誤: 檔案 "{name}" 使用 UTF-16 LE 編碼。\n請另存為 UTF-8 格式。',
        encodingUtf16BE: '編碼錯誤: 檔案 "{name}" 使用 UTF-16 BE 編碼。\n請另存為 UTF-8 格式。',
        encodingInvalid: '編碼錯誤: 檔案 "{name}" 不是 UTF-8 編碼。\n請確保檔案以 UTF-8 格式儲存。'
    },

    download: {
        downloading: '下載中...',
        success: '下載成功',
        error: '下載失敗',
        downloadLabel: '下載 {label}',
        downloadAriaLabel: '下載 {label} 檔案'
    },

    // ===== form/ =====

    /** DatePicker — 日期選擇器 */
    datePicker: {
        placeholder: '請選擇日期',
        weekdays: {
            sun: '日', mon: '一', tue: '二', wed: '三',
            thu: '四', fri: '五', sat: '六'
        },
        rocYear: '民國 {year} 年'
    },

    /** TimePicker — 時間選擇器 */
    timePicker: {
        placeholder: '請選擇時間',
        hour: '小時',
        minute: '分鐘',
        confirm: '確認'
    },

    /** Dropdown — 下拉選單 */
    dropdown: {
        placeholder: '請選擇',
        emptyText: '無符合項目'
    },

    /** MultiSelectDropdown — 多選下拉 */
    multiSelect: {
        placeholder: '請選擇',
        emptyText: '無符合項目',
        modalTitle: '選擇項目',
        expandAll: '展開全部選項',
        searchPlaceholder: '搜尋選項...',
        selectAll: '全選',
        deselectAll: '全不選',
        cancel: '取消',
        confirm: '確定',
        selectedCount: '已選 {count}{max} 項'
    },

    /** SearchForm — 搜尋表單 */
    searchForm: {
        searchText: '搜尋',
        resetText: '重設',
        expand: '展開 ▼',
        collapse: '收合 ▲',
        selectPlaceholder: '請選擇',
        datePlaceholder: '選擇日期',
        startDate: '開始日期',
        endDate: '結束日期',
        dateSeparator: '至',
        requiredError: '此欄位為必填'
    },

    // ===== layout/ =====

    /** DataTable — 資料表格 */
    dataTable: {
        rowsPerPage: '每頁筆數:',
        displayRows: '共',
        noMatch: '無查詢結果',
        selectedUnit: '筆',
        firstPage: '第一頁',
        prevPage: '上一頁',
        nextPage: '下一頁',
        lastPage: '最後一頁'
    },

    /** ModalPanel — 模態面板 */
    modalPanel: {
        confirmTitle: '確認',
        alertTitle: '提示',
        promptTitle: '輸入',
        confirmText: '確認',
        cancelText: '取消',
        okText: '確定'
    },

    /** PhotoWall — 照片牆 */
    photoWall: {
        downloadSelected: '下載選取 ({count})',
        packing: '打包中...',
        packError: '打包失敗，請重試',
        deleteConfirmTitle: '刪除確認',
        deleteConfirmMessage: '確定要刪除這張照片嗎？',
        confirmBtn: '確定',
        cancelBtn: '取消',
        doubleConfirmTitle: '再次確認',
        doubleConfirmMessage: '請輸入 "是" 以確認刪除操作：',
        doubleConfirmPlaceholder: '是',
        doubleConfirmBtn: '確認刪除',
        doubleConfirmKeyword: '是'
    },

    /** DocumentWall — 文件牆 */
    documentWall: {
        downloadSelected: '下載選取 ({count})',
        packing: '打包中...',
        packError: '打包下載失敗',
        noDescription: '無',
        descriptionPrefix: '文件說明：',
        editBtn: '編輯',
        descBtn: '說明',
        downloadBtn: '下載',
        deleteConfirmTitle: '刪除確認',
        deleteConfirmMessage: '確定要刪除文件 "{title}" 嗎？',
        confirmBtn: '確定',
        cancelBtn: '取消',
        doubleConfirmTitle: '再次確認',
        doubleConfirmMessage: '請輸入 "是" 以確認刪除操作：',
        doubleConfirmPlaceholder: '是',
        doubleConfirmBtn: '確認刪除',
        doubleConfirmKeyword: '是'
    },

    /** InfoPanel — 資訊面板 */
    infoPanel: {
        untitledPanel: '未命名面板',
        chartPlaceholder: '圖表區域'
    },

    /** SideMenu — 側邊選單 */
    sideMenu: {
        expand: '展開選單',
        collapse: '收合選單'
    },

    /** WorkflowPanel — 流程面板（流程階段名稱為領域特定，但 UI 文字需要） */
    workflowPanel: {
        currentBadge: '目前',
        pending: '待定'
    },

    // ===== input/ =====

    /** AddressInput — 地址輸入 */
    addressInput: {
        cityLabel: '縣市',
        cityPlaceholder: '請選擇縣市',
        districtLabel: '行政區',
        districtPlaceholder: '請選擇行政區',
        detailLabel: '詳細地址',
        detailPlaceholder: '請輸入街道巷弄號碼'
    },

    /** AddressListInput — 地址列表 */
    addressListInput: {
        title: '地址列表',
        addButton: '新增地址'
    },

    /** DateTimeInput — 日期時間 */
    dateTimeInput: {
        dateLabel: '日期',
        timeLabel: '時間'
    },

    /** PersonInfoList — 人員資料 */
    personInfoList: {
        title: '個人基本資料',
        addButton: '新增人員',
        nameLabel: '姓名',
        namePlaceholder: '請輸入姓名',
        genderLabel: '性別',
        genderOptions: { male: '男', female: '女', other: '其他' },
        ageLabel: '年齡',
        idLabel: '身分證號',
        idPlaceholder: '請輸入號碼',
        otherIdLabel: '其他證號'
    },

    /** OrganizationInput — 組織單位 */
    organizationInput: {
        level1Label: '一級單位',
        level2Label: '二級單位',
        level3Label: '三級單位',
        level4Label: '四級單位',
        placeholder: '請選擇'
    },

    /** StudentInput — 學生資訊 */
    studentInput: {
        checkboxLabel: '是否為在學學生',
        statusLabel: '學籍身份',
        schoolLabel: '學校名稱',
        schoolPlaceholder: '請輸入就讀學校'
    },

    /** ListInput — 列表輸入 */
    listInput: {
        addButton: '新增項目',
        csvTemplate: '下載 CSV 範本',
        dragToSort: '拖曳排序',
        moveUp: '上移',
        moveDown: '下移',
        removeItem: '移除項目',
        selectPlaceholder: '請選擇'
    },

    /** SocialMediaList — 社群帳號 */
    socialMediaList: {
        title: '社群軟體列表',
        addButton: '新增帳號',
        placeholder: '請輸入 ID 或連結',
        other: '其他'
    },

    /** PhoneListInput — 電話列表 */
    phoneListInput: {
        title: '電話列表',
        addButton: '新增電話',
        placeholder: '請輸入電話號碼',
        types: {
            mobile: '手機',
            landline: '市話',
            company: '公司',
            fax: '傳真'
        }
    },

    /** ChainedInput — 連動輸入 */
    chainedInput: {
        placeholder: '請選擇',
        checkboxYes: '是',
        loading: '載入中...',
        noOptions: '無選項',
        loadError: '載入失敗'
    },

    // ===== viz/ =====

    /** DrawingBoard — 繪圖板 */
    drawingBoard: {
        pen: '畫筆',
        eraser: '橡皮擦',
        line: '直線',
        highlighter: '螢光筆',
        clear: '清除',
        exportPng: '匯出 PNG',
        colorLabel: '顏色：',
        thicknessLabel: '粗細：',
        opacityLabel: '透明度：'
    },

    /** WebPainter — 地圖標註編輯器 */
    webPainter: {
        layerManage: '📜 圖層管理',
        layerNameLabel: '圖層名稱:',
        defaultLayerName: '圖層 {n}',
        confirmDeleteLayer: '確定刪除其餘圖層 [{name}]？圖層內標註將一併刪除。',
        uploadBg: '上傳底圖',
        deleteBtn: '刪除',
        confirmClearAll: '確定要清除所有標註？',
        fontSizeLabel: '字體大小：',
        fontFamilyLabel: '字型：',
        fontMsJhengHei: '微軟正黑體',
        fontMingLiU: '新細明體',
        textColorLabel: '文字顏色',
        strokeColorLabel: '線條顏色',
        fillColorLabel: '填充顏色',
        strokeWidthLabel: '線條粗細：',
        promptText: '請輸入文字：',
        promptPin: '請輸入打點標註文字：',
        editTextTitle: '編輯文字：',
        exportFilename: '地圖編輯-',
        exportSuccess: '✅ PNG 已匯出（含元數據）',
        metadataLabel: '📊 元數據:',
        invalidFormat: '無效的資料格式',
        unnamed: '未命名',
        defaultLayer: '預設圖層',
        loadFailed: '載入資料失敗 (Security Check Failed):',
        configFilename: '地圖配置-',
        loadPngSuccess: '✅ 已載入 PNG 與元數據',
        restoreFailed: '還原 PNG 元數據失敗，僅載入標註',
        pngNoMeta: 'ℹ️ PNG 無元數據',
        loadImageFailed: '載入圖片失敗',
        readFileFailed: '讀取檔案失敗',
        parseMetaFailed: '解析元數據失敗:',
        applyCrop: '✅ 套用裁切',
        cancelCrop: '❌ 取消',
        selectTool: '選擇',
        textTool: '文字',
        pinTool: '打點',
        penTool: '畫筆',
        rectTool: '矩形',
        circleTool: '圓形',
        lineTool: '線條',
        arrowTool: '箭頭',
        clearAllBtn: '🧹 清空',
        exportPngBtn: '💾 匯出 PNG',
        saveJsonBtn: '📄 存 JSON',
        layerBtn: '📜 圖層'
    },

    /** HierarchyChart — 組織圖 */
    hierarchyChart: {
        orgSuffix: '組織'
    },

    /** RelationChart — 關係圖 */
    relationChart: {
        hoverTooltip: '顯示詳細資訊 (Hover)'
    },

    // ===== editor/ =====

    /** WebTextEditor — 文字編輯器 */
    webTextEditor: {
        placeholder: '請在此輸入內容...',
        pageBreakLine: '--- 分頁符 (列印時此處分頁) ---',
        headerArea: '頁首區',
        footerArea: '頁尾區',
        tabCommon: '常用',
        tabInsert: '插入',
        tabLayout: '版面配置',
        marginNarrow: '邊界 (窄)',
        marginNormal: '邊界 (標準)',
        marginWide: '邊界 (寬)',
        tabTools: '工具',
        insertImage: '插入圖片',
        lineSpacing: '行間距',
        lineSpacingLabel: '行距 {label}',
        promptLink: '請輸入連結網址：',
        imageOnly: '只允許上傳圖片檔案',
        defaultDrawingLayer: '預設圖層',
        clickEditDrawing: '點擊編輯圖片/繪圖',
        clickEditIllustration: '點擊編輯插圖',
        drawingMode: '🎨 繪圖板編輯模式',
        cancelBtn: '取消',
        doneBtn: '✅ 完成並更新',
        updateFailed: '更新失敗，請檢查 console',
        promptRows: '請輸入行數 (1-20)：',
        promptCols: '請輸入列數 (1-10)：',
        headerCell: '標題 {n}',
        bodyCell: '單元格',
        fontFamily: '字型',
        fontSize: '字體大小 (px)',
        bold: '粗體',
        italic: '斜體',
        underline: '底線',
        convertToLink: '轉換為連結',
        unsafeLink: '連結含有不安全的內容',
        clearFormat: '清除格式',
        editContent: '編輯內容',
        alignLeft: '靠左',
        alignCenter: '置中',
        alignRight: '靠右',
        alignFull: '全寬',
        addRow: '添加一行',
        addCol: '添加一列',
        deleteRow: '刪除一行',
        deleteCol: '刪除一列',
        deleteTable: '刪除表格',
        confirmDeleteTable: '確定刪除整個表格？',
        newCell: '新儲存格',
        newHeader: '新標題',
        keepOneRow: '至少需要保留一行',
        autoSaved: '自動存檔於 {time}',
        headerPlaceholder: '輸入頁首內容...',
        footerPlaceholder: '輸入頁尾內容...',
        pageIndicator: '第 {n} 頁',
        confirmClearAll: '確定要清空整份文件的內容嗎？此動作無法復原。',
        searchPlaceholder: '搜尋文字...',
        prevMatch: '上一個',
        nextMatch: '下一個',
        replacePlaceholder: '取代為...',
        matchCount: '{count} 個結果',
        replacedCount: '已取代 {count} 個',
        printError: '無法開啟列印視窗，請檢查瀏覽器彈出視窗設定',
        noHeadings: '文件中沒有找到任何標題 (H1-H6)',
        noHeadingsForToc: '文件中沒有找到任何標題，無法生成目錄',
        tocInserted: '目錄已插入到文件開頭',
        draftFound: '發現自動儲存的草稿 ({time})，是否恢復？'
    },

    // ===== social/ =====

    /** FeedCard — 動態卡片 */
    feedCard: {
        justNow: '剛剛',
        minutesAgo: '{n} 分鐘前',
        hoursAgo: '{n} 小時前',
        daysAgo: '{n} 天前',
        imageAlt: '圖片{n}',
        showMore: '...查看更多',
        relatedCount: '👥 {count} 位關聯人',
        viewDetail: '查看詳情 →'
    },

    /** Timeline — 時間軸 */
    timeline: {
        emptyText: '暫無活動紀錄',
        monthGroup: '{year} 年 {month} 月',
        unknownTime: '未知時間'
    },

    /** OSMMapEditor — 通用 OSM 地圖編輯器 */
    osmMapEditor: {
        toggleMap: '開啟地圖',
        mapTitle: 'OpenStreetMap 地圖',
        close: '關閉',
        captureMap: '截取地圖',
        captureHint: '截取範圍',
        captureSuccess: '地圖截圖成功',
        captureFailed: '截圖失敗',
        mapNotReady: '地圖尚未載入完成',
        mapInitError: '地圖初始化失敗',
        hintDrag: '拖動地圖調整位置',
        hintZoom: '滾輪縮放',
        hintCapture: '截取框內內容',
        hintMeasure: '使用工具列測量',
        latitude: '緯度',
        longitude: '經度',
        coordDD: 'DD (十進位)',
        coordDMS: 'DMS (度分秒)',
        compass: '指北針 — 點擊重置方向',
        distanceHint: '點擊地圖添加測量點，雙擊結束',
        areaHint: '點擊地圖添加頂點，雙擊閉合',
        totalDistance: '總距離',
        area: '面積',
        distanceResult: '距離測量結果',
        areaResult: '面積測量結果',
        pointCount: '測量點數',
        vertexCount: '頂點數',
        needTwoPoints: '請至少點擊兩個點',
        needThreePoints: '請至少點擊三個點',
        needMorePoints: '請繼續點擊 (至少 3 點)',
        exportGeoJSON: '匯出 GeoJSON',
        importGeoJSON: '匯入 GeoJSON',
        exportSuccess: '成功匯出 {count} 個圖形',
        importSuccess: '成功匯入 {count} 個圖形',
        importFailed: '匯入失敗',
        openMapFirst: '請先開啟地圖',
        switchLayer: '切換圖層',
        meters: '公尺',
        kilometers: '公里',
        squareMeters: '平方公尺',
        hectares: '公頃',
        squareKilometers: '平方公里'
    }
};
