/**
 * English locale (en)
 * Built-in language pack for component library
 */
export default {
    // ===== common/ =====

    /** BasicButton */
    basicButton: {
        delete: 'Delete',
        confirm: 'Confirm',
        yes: 'Yes',
        cancel: 'Cancel',
        no: 'No',
        done: 'Done',
        close: 'Close',
        search: 'Search',
        clear: 'Clear',
        reset: 'Reset',
        save: 'Save',
        apply: 'Apply',
        copy: 'Copy',
        refresh: 'Refresh',
        addRow: 'Add Row',
        selectAll: 'Select All',
        deselectAll: 'Deselect All',
        back: 'Back',
        next: 'Next',
        prev: 'Previous',
        expandAll: 'Expand All',
        collapseAll: 'Collapse All',
        confirmLabel: 'OK',
        cancelLabel: 'Cancel'
    },

    /** ActionButton */
    actionButton: {
        add: 'Add',
        delete: 'Delete',
        edit: 'Edit',
        detail: 'Detail',
        submit: 'Submit',
        reject: 'Reject',
        archive: 'Archive',
        merge: 'Merge',
        verify: 'Verify',
        withdraw: 'Withdraw',
        report: 'Report',
        transfer: 'Transfer',
        approve: 'Approve',
        modify: 'Modify',
        confirmMessage: 'Are you sure you want to proceed?',
        confirmDelete: 'Are you sure you want to delete?',
        confirmReject: 'Are you sure you want to reject?'
    },

    /** AuthButton */
    authButton: {
        login: 'Login',
        logout: 'Logout',
        confirmLogout: 'Are you sure you want to logout?'
    },

    /** EditorButton */
    editorButton: {
        bold: 'Bold',
        italic: 'Italic',
        underline: 'Underline',
        strikethrough: 'Strikethrough',
        subscript: 'Subscript',
        superscript: 'Superscript',
        heading1: 'Heading 1',
        heading2: 'Heading 2',
        heading3: 'Heading 3',
        paragraph: 'Paragraph',
        quote: 'Quote',
        code: 'Code',
        alignLeft: 'Align Left',
        alignCenter: 'Align Center',
        alignRight: 'Align Right',
        alignJustify: 'Justify',
        listBullet: 'Bullet List',
        listNumber: 'Numbered List',
        indent: 'Indent',
        outdent: 'Outdent',
        undo: 'Undo',
        redo: 'Redo',
        link: 'Link',
        image: 'Image',
        table: 'Table',
        line: 'Horizontal Line',
        pageBreak: 'Page Break',
        pen: 'Pen',
        eraser: 'Eraser',
        lineTool: 'Line',
        highlighter: 'Highlighter',
        rect: 'Rectangle',
        circle: 'Circle',
        arrow: 'Arrow',
        text: 'Text',
        select: 'Select',
        measureDistance: 'Measure Distance',
        measureArea: 'Measure Area',
        coordinate: 'Coordinate',
        exportPdf: 'Export PDF',
        exportWord: 'Export Word',
        exportMarkdown: 'Export Markdown',
        exportPng: 'Export PNG',
        exportJson: 'Export JSON',
        search: 'Search',
        replace: 'Replace',
        fullscreen: 'Fullscreen',
        clear: 'Clear',
        clearAll: 'Clear All',
        removeFormat: 'Remove Format',
        copy: 'Copy',
        paste: 'Paste',
        cut: 'Cut',
        toc: 'Table of Contents',
        settings: 'Settings',
        layers: 'Layers',
        zoomIn: 'Zoom In',
        zoomOut: 'Zoom Out',
        insertDrawing: 'Drawing',
        insertToc: 'Table of Contents',
        header: 'Header',
        footer: 'Footer',
        pageNumber: 'Page Number',
        margin: 'Margin',
        generateToc: 'Generate TOC'
    },

    /** Notification */
    notification: {
        success: 'Success',
        error: 'Error',
        warning: 'Warning',
        info: 'Info'
    },

    /** SimpleDialog */
    dialog: {
        alert: 'Alert',
        confirm: 'Confirm',
        confirmBtn: 'OK',
        cancelBtn: 'Cancel'
    },

    /** Pagination */
    pagination: {
        prev: 'Previous',
        next: 'Next',
        first: 'First Page',
        last: 'Last Page',
        totalPrefix: '',
        totalSuffix: ' records',
        perPage: ' / page',
        goTo: 'Go to',
        page: '',
        jump: 'Go'
    },

    /** LoadingSpinner */
    loadingSpinner: {
        text: 'Loading...'
    },

    /** Breadcrumb */
    breadcrumb: {
        home: 'Home'
    },

    /** Upload / Download */
    upload: {
        dragHint: 'Drag files here or click to upload',
        uploading: 'Uploading {percent}%',
        success: 'Upload successful',
        error: 'Upload failed',
        maxSize: 'File size exceeds the limit ({max})',
        maxCount: 'Maximum {max} files allowed',
        uploadLabel: 'Upload {label}',
        uploadAriaLabel: 'Upload {label} file',
        securityPathTraversal: 'Security alert: File name "{name}" contains invalid characters (path traversal risk).',
        fileTooLarge: 'File "{name}" exceeds size limit (max {max}).',
        formatMismatch: 'Security alert: File "{name}" content does not match expected format.\nPlease ensure you upload the correct file type.',
        encodingUtf16LE: 'Encoding error: File "{name}" uses UTF-16 LE encoding.\nPlease save as UTF-8.',
        encodingUtf16BE: 'Encoding error: File "{name}" uses UTF-16 BE encoding.\nPlease save as UTF-8.',
        encodingInvalid: 'Encoding error: File "{name}" is not valid UTF-8.\nPlease ensure the file is saved in UTF-8 format.'
    },

    download: {
        downloading: 'Downloading...',
        success: 'Download successful',
        error: 'Download failed',
        downloadLabel: 'Download {label}',
        downloadAriaLabel: 'Download {label} file'
    },

    // ===== form/ =====

    /** DatePicker */
    datePicker: {
        placeholder: 'Select date',
        weekdays: {
            sun: 'Sun', mon: 'Mon', tue: 'Tue', wed: 'Wed',
            thu: 'Thu', fri: 'Fri', sat: 'Sat'
        },
        rocYear: 'ROC Year {year}'
    },

    /** TimePicker */
    timePicker: {
        placeholder: 'Select time',
        hour: 'Hour',
        minute: 'Minute',
        confirm: 'OK'
    },

    /** Dropdown */
    dropdown: {
        placeholder: 'Select',
        emptyText: 'No matching items'
    },

    /** MultiSelectDropdown */
    multiSelect: {
        placeholder: 'Select',
        emptyText: 'No matching items',
        modalTitle: 'Select Items',
        expandAll: 'Expand all options',
        searchPlaceholder: 'Search options...',
        selectAll: 'Select All',
        deselectAll: 'Deselect All',
        cancel: 'Cancel',
        confirm: 'OK',
        selectedCount: '{count}{max} selected'
    },

    /** SearchForm */
    searchForm: {
        searchText: 'Search',
        resetText: 'Reset',
        expand: 'Expand ▼',
        collapse: 'Collapse ▲',
        selectPlaceholder: 'Select',
        datePlaceholder: 'Select date',
        startDate: 'Start Date',
        endDate: 'End Date',
        dateSeparator: 'to',
        requiredError: 'This field is required'
    },

    // ===== layout/ =====

    /** DataTable */
    dataTable: {
        rowsPerPage: 'Rows per page:',
        displayRows: 'Total',
        noMatch: 'No matching records',
        selectedUnit: 'rows',
        firstPage: 'First Page',
        prevPage: 'Previous',
        nextPage: 'Next',
        lastPage: 'Last Page'
    },

    /** ModalPanel */
    modalPanel: {
        confirmTitle: 'Confirm',
        alertTitle: 'Alert',
        promptTitle: 'Input',
        confirmText: 'Confirm',
        cancelText: 'Cancel',
        okText: 'OK'
    },

    /** PhotoWall */
    photoWall: {
        downloadSelected: 'Download Selected ({count})',
        packing: 'Packing...',
        packError: 'Packing failed, please retry',
        deleteConfirmTitle: 'Delete Confirmation',
        deleteConfirmMessage: 'Are you sure you want to delete this photo?',
        confirmBtn: 'OK',
        cancelBtn: 'Cancel',
        doubleConfirmTitle: 'Confirm Again',
        doubleConfirmMessage: 'Type "yes" to confirm deletion:',
        doubleConfirmPlaceholder: 'yes',
        doubleConfirmBtn: 'Confirm Delete',
        doubleConfirmKeyword: 'yes'
    },

    /** DocumentWall */
    documentWall: {
        downloadSelected: 'Download Selected ({count})',
        packing: 'Packing...',
        packError: 'Download packing failed',
        noDescription: 'None',
        descriptionPrefix: 'Description: ',
        editBtn: 'Edit',
        descBtn: 'Description',
        downloadBtn: 'Download',
        deleteConfirmTitle: 'Delete Confirmation',
        deleteConfirmMessage: 'Are you sure you want to delete "{title}"?',
        confirmBtn: 'OK',
        cancelBtn: 'Cancel',
        doubleConfirmTitle: 'Confirm Again',
        doubleConfirmMessage: 'Type "yes" to confirm deletion:',
        doubleConfirmPlaceholder: 'yes',
        doubleConfirmBtn: 'Confirm Delete',
        doubleConfirmKeyword: 'yes'
    },

    /** InfoPanel */
    infoPanel: {
        untitledPanel: 'Untitled Panel',
        chartPlaceholder: 'Chart Area'
    },

    /** SideMenu */
    sideMenu: {
        expand: 'Expand Menu',
        collapse: 'Collapse Menu'
    },

    /** WorkflowPanel */
    workflowPanel: {
        currentBadge: 'Current',
        pending: 'Pending'
    },

    // ===== input/ =====

    /** AddressInput */
    addressInput: {
        cityLabel: 'City / County',
        cityPlaceholder: 'Select city',
        districtLabel: 'District',
        districtPlaceholder: 'Select district',
        detailLabel: 'Address',
        detailPlaceholder: 'Enter street address'
    },

    /** AddressListInput */
    addressListInput: {
        title: 'Address List',
        addButton: 'Add Address'
    },

    /** DateTimeInput */
    dateTimeInput: {
        dateLabel: 'Date',
        timeLabel: 'Time'
    },

    /** PersonInfoList */
    personInfoList: {
        title: 'Personal Information',
        addButton: 'Add Person',
        nameLabel: 'Name',
        namePlaceholder: 'Enter name',
        genderLabel: 'Gender',
        genderOptions: { male: 'Male', female: 'Female', other: 'Other' },
        ageLabel: 'Age',
        idLabel: 'ID Number',
        idPlaceholder: 'Enter ID number',
        otherIdLabel: 'Other ID'
    },

    /** OrganizationInput */
    organizationInput: {
        level1Label: 'Level 1',
        level2Label: 'Level 2',
        level3Label: 'Level 3',
        level4Label: 'Level 4',
        placeholder: 'Select'
    },

    /** StudentInput */
    studentInput: {
        checkboxLabel: 'Currently enrolled',
        statusLabel: 'Student Status',
        schoolLabel: 'School Name',
        schoolPlaceholder: 'Enter school name'
    },

    /** ListInput */
    listInput: {
        addButton: 'Add Item',
        csvTemplate: 'Download CSV Template',
        dragToSort: 'Drag to reorder',
        moveUp: 'Move Up',
        moveDown: 'Move Down',
        removeItem: 'Remove Item',
        selectPlaceholder: 'Select'
    },

    /** SocialMediaList */
    socialMediaList: {
        title: 'Social Media',
        addButton: 'Add Account',
        placeholder: 'Enter ID or link',
        other: 'Other'
    },

    /** PhoneListInput */
    phoneListInput: {
        title: 'Phone List',
        addButton: 'Add Phone',
        placeholder: 'Enter phone number',
        types: {
            mobile: 'Mobile',
            landline: 'Landline',
            company: 'Office',
            fax: 'Fax'
        }
    },

    /** ChainedInput */
    chainedInput: {
        placeholder: 'Select',
        checkboxYes: 'Yes',
        loading: 'Loading...',
        noOptions: 'No options',
        loadError: 'Failed to load'
    },

    // ===== viz/ =====

    /** DrawingBoard */
    drawingBoard: {
        pen: 'Pen',
        eraser: 'Eraser',
        line: 'Line',
        highlighter: 'Highlighter',
        clear: 'Clear',
        exportPng: 'Export PNG',
        colorLabel: 'Color:',
        thicknessLabel: 'Thickness:',
        opacityLabel: 'Opacity:'
    },

    /** WebPainter */
    webPainter: {
        layerManage: '📜 Layer Manager',
        layerNameLabel: 'Layer name:',
        defaultLayerName: 'Layer {n}',
        confirmDeleteLayer: 'Delete layer [{name}]? All annotations in this layer will also be deleted.',
        uploadBg: 'Upload Background',
        deleteBtn: 'Delete',
        confirmClearAll: 'Clear all annotations?',
        fontSizeLabel: 'Font size:',
        fontFamilyLabel: 'Font:',
        fontMsJhengHei: 'Microsoft JhengHei',
        fontMingLiU: 'MingLiU',
        textColorLabel: 'Text color',
        strokeColorLabel: 'Stroke color',
        fillColorLabel: 'Fill color',
        strokeWidthLabel: 'Stroke width:',
        promptText: 'Enter text:',
        promptPin: 'Enter pin annotation text:',
        editTextTitle: 'Edit text:',
        exportFilename: 'map-editor-',
        exportSuccess: '✅ PNG exported (with metadata)',
        metadataLabel: '📊 Metadata:',
        invalidFormat: 'Invalid data format',
        unnamed: 'Unnamed',
        defaultLayer: 'Default Layer',
        loadFailed: 'Load failed (Security Check Failed):',
        configFilename: 'map-config-',
        loadPngSuccess: '✅ PNG and metadata loaded',
        restoreFailed: 'Failed to restore PNG metadata, annotations only',
        pngNoMeta: 'ℹ️ PNG has no metadata',
        loadImageFailed: 'Failed to load image',
        readFileFailed: 'Failed to read file',
        parseMetaFailed: 'Failed to parse metadata:',
        applyCrop: '✅ Apply Crop',
        cancelCrop: '❌ Cancel',
        selectTool: 'Select',
        textTool: 'Text',
        pinTool: 'Pin',
        penTool: 'Pen',
        rectTool: 'Rectangle',
        circleTool: 'Circle',
        lineTool: 'Line',
        arrowTool: 'Arrow',
        clearAllBtn: '🧹 Clear All',
        exportPngBtn: '💾 Export PNG',
        saveJsonBtn: '📄 Save JSON',
        layerBtn: '📜 Layers'
    },

    /** HierarchyChart */
    hierarchyChart: {
        orgSuffix: 'Organization'
    },

    /** RelationChart */
    relationChart: {
        hoverTooltip: 'Show details (Hover)'
    },

    // ===== editor/ =====

    /** WebTextEditor */
    webTextEditor: {
        placeholder: 'Type here...',
        pageBreakLine: '--- Page Break (printed page break here) ---',
        headerArea: 'Header Area',
        footerArea: 'Footer Area',
        tabCommon: 'Home',
        tabInsert: 'Insert',
        tabLayout: 'Layout',
        marginNarrow: 'Margin (Narrow)',
        marginNormal: 'Margin (Normal)',
        marginWide: 'Margin (Wide)',
        tabTools: 'Tools',
        insertImage: 'Insert Image',
        lineSpacing: 'Line Spacing',
        lineSpacingLabel: 'Spacing {label}',
        promptLink: 'Enter link URL:',
        imageOnly: 'Only image files are allowed',
        defaultDrawingLayer: 'Default Layer',
        clickEditDrawing: 'Click to edit image/drawing',
        clickEditIllustration: 'Click to edit illustration',
        drawingMode: '🎨 Drawing Board Edit Mode',
        cancelBtn: 'Cancel',
        doneBtn: '✅ Done & Update',
        updateFailed: 'Update failed, check console',
        promptRows: 'Enter number of rows (1-20):',
        promptCols: 'Enter number of columns (1-10):',
        headerCell: 'Header {n}',
        bodyCell: 'Cell',
        fontFamily: 'Font',
        fontSize: 'Font size (px)',
        bold: 'Bold',
        italic: 'Italic',
        underline: 'Underline',
        convertToLink: 'Convert to link',
        unsafeLink: 'Link contains unsafe content',
        clearFormat: 'Clear Format',
        editContent: 'Edit Content',
        alignLeft: 'Left',
        alignCenter: 'Center',
        alignRight: 'Right',
        alignFull: 'Full Width',
        addRow: 'Add Row',
        addCol: 'Add Column',
        deleteRow: 'Delete Row',
        deleteCol: 'Delete Column',
        deleteTable: 'Delete Table',
        confirmDeleteTable: 'Delete the entire table?',
        newCell: 'New Cell',
        newHeader: 'New Header',
        keepOneRow: 'At least one row must be kept',
        autoSaved: 'Auto-saved at {time}',
        headerPlaceholder: 'Enter header content...',
        footerPlaceholder: 'Enter footer content...',
        pageIndicator: 'Page {n}',
        confirmClearAll: 'Clear all document content? This action cannot be undone.',
        searchPlaceholder: 'Search text...',
        prevMatch: 'Previous',
        nextMatch: 'Next',
        replacePlaceholder: 'Replace with...',
        matchCount: '{count} results',
        replacedCount: 'Replaced {count}',
        printError: 'Cannot open print window, check browser popup settings',
        noHeadings: 'No headings (H1-H6) found in document',
        noHeadingsForToc: 'No headings found, cannot generate table of contents',
        tocInserted: 'Table of contents inserted at document beginning',
        draftFound: 'Auto-saved draft found ({time}), restore?'
    },

    // ===== social/ =====

    /** FeedCard */
    feedCard: {
        justNow: 'Just now',
        minutesAgo: '{n} minutes ago',
        hoursAgo: '{n} hours ago',
        daysAgo: '{n} days ago',
        imageAlt: 'Image {n}',
        showMore: '...Show more',
        relatedCount: '👥 {count} related people',
        viewDetail: 'View details →'
    },

    /** Timeline */
    timeline: {
        emptyText: 'No activity records',
        monthGroup: '{month} {year}',
        unknownTime: 'Unknown time'
    },

    /** OSMMapEditor */
    osmMapEditor: {
        toggleMap: 'Map',
        mapTitle: 'OpenStreetMap',
        close: 'Close',
        captureMap: 'Capture Map',
        captureHint: 'Capture Area',
        captureSuccess: 'Map captured successfully',
        captureFailed: 'Capture failed',
        mapNotReady: 'Map not loaded yet',
        mapInitError: 'Map initialization failed',
        hintDrag: 'Drag to adjust position',
        hintZoom: 'Scroll to zoom',
        hintCapture: 'Capture frame content',
        hintMeasure: 'Use toolbar to measure',
        latitude: 'Lat',
        longitude: 'Lng',
        coordDD: 'DD (Decimal)',
        coordDMS: 'DMS',
        compass: 'Compass — Click to reset',
        distanceHint: 'Click to add points, double-click to finish',
        areaHint: 'Click to add vertices, double-click to close',
        totalDistance: 'Total Distance',
        area: 'Area',
        distanceResult: 'Distance Result',
        areaResult: 'Area Result',
        pointCount: 'Points',
        vertexCount: 'Vertices',
        needTwoPoints: 'Need at least 2 points',
        needThreePoints: 'Need at least 3 points',
        needMorePoints: 'Keep clicking (min 3 points)',
        exportGeoJSON: 'Export GeoJSON',
        importGeoJSON: 'Import GeoJSON',
        exportSuccess: 'Exported {count} features',
        importSuccess: 'Imported {count} features',
        importFailed: 'Import failed',
        openMapFirst: 'Please open the map first',
        switchLayer: 'Switch Layer',
        meters: 'm',
        kilometers: 'km',
        squareMeters: 'm²',
        hectares: 'ha',
        squareKilometers: 'km²'
    }
};
