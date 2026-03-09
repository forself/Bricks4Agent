/**
 * SimpleZip
 * 一個輕量級、純 JS 實作的 ZIP 打包工具 (僅儲存，不壓縮)
 * 用於在瀏覽器端將多個檔案打包下載，不依賴任何外部函式庫
 */

class SimpleZip {
    constructor() {
        this.files = [];
    }

    /**
     * 加入檔案
     * @param {string} filename 檔名
     * @param {Blob|Uint8Array|string} content 檔案內容
     */
    addFile(filename, content) {
        this.files.push({
            filename,
            content
        });
    }

    /**
     * 產生 ZIP Blob
     * @returns {Promise<Blob>}
     */
    async generateAsync() {
        const parts = [];
        const centralDirectory = [];
        let offset = 0;

        // CRC32 table
        const crcTable = new Int32Array(256);
        for (let i = 0; i < 256; i++) {
            let c = i;
            for (let k = 0; k < 8; k++) {
                c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
            }
            crcTable[i] = c;
        }
        const crc32 = (data) => {
            let crc = -1;
            for (let i = 0; i < data.length; i++) {
                crc = (crc >>> 8) ^ crcTable[(crc ^ data[i]) & 0xFF];
            }
            return (crc ^ -1) >>> 0;
        };

        // Helper to Convert String to Uint8Array
        const strToUint8 = (str) => new TextEncoder().encode(str);

        // Helper to create DataView/Uint8Array buffer
        const createBuffer = (size) => {
            const buffer = new ArrayBuffer(size);
            return {
                view: new DataView(buffer),
                uint8: new Uint8Array(buffer)
            };
        };

        for (const file of this.files) {
            let fileData;
            if (file.content instanceof Blob) {
                fileData = new Uint8Array(await file.content.arrayBuffer());
            } else if (typeof file.content === 'string') {
                fileData = strToUint8(file.content);
            } else {
                fileData = file.content;
            }

            const filenameBytes = strToUint8(file.filename);
            const crc = crc32(fileData);
            const size = fileData.length;
            const time = new Date();

            // MSDOS Date/Time
            const dosTime =
                ((time.getFullYear() - 1980) << 25) |
                ((time.getMonth() + 1) << 21) |
                (time.getDate() << 16) |
                (time.getHours() << 11) |
                (time.getMinutes() << 5) |
                (time.getSeconds() >> 1);

            // Local File Header
            // 30 bytes + filename length
            const headerSize = 30 + filenameBytes.length;
            const header = createBuffer(headerSize);
            const dv = header.view;

            dv.setUint32(0, 0x04034b50, true); // Signature
            dv.setUint16(4, 0x000A, true);     // Version needed (1.0)
            dv.setUint16(6, 0x0000, true);     // Flags (0)
            dv.setUint16(8, 0x0000, true);     // Compression (0 = store)
            dv.setUint32(10, dosTime, true);   // Mod time/date
            dv.setUint32(14, crc, true);       // CRC32
            dv.setUint32(18, size, true);      // Compressed size
            dv.setUint32(22, size, true);      // Uncompressed size
            dv.setUint16(26, filenameBytes.length, true); // Filename length
            dv.setUint16(28, 0, true);         // Extra field length

            header.uint8.set(filenameBytes, 30);

            parts.push(header.uint8);
            parts.push(fileData);

            // Record for Central Directory
            centralDirectory.push({
                filenameBytes,
                crc,
                size,
                dosTime,
                offset,
                attributes: 0
            });

            offset += headerSize + size;
        }

        const centralDirStart = offset;

        // Central Directory
        for (const file of centralDirectory) {
            // 46 bytes + filename length
            const headerSize = 46 + file.filenameBytes.length;
            const header = createBuffer(headerSize);
            const dv = header.view;

            dv.setUint32(0, 0x02014b50, true); // Signature
            dv.setUint16(4, 0x0014, true);     // Version made by (2.0)
            dv.setUint16(6, 0x000A, true);     // Version needed (1.0)
            dv.setUint16(8, 0x0000, true);     // Flags
            dv.setUint16(10, 0x0000, true);    // Compression (0 = store)
            dv.setUint32(12, file.dosTime, true); // Mod time/date
            dv.setUint32(16, file.crc, true);     // CRC32
            dv.setUint32(20, file.size, true);    // Compressed size
            dv.setUint32(24, file.size, true);    // Uncompressed size
            dv.setUint16(28, file.filenameBytes.length, true); // Filename length
            dv.setUint16(30, 0, true);         // Extra field length
            dv.setUint16(32, 0, true);         // Comment length
            dv.setUint16(34, 0, true);         // Disk number start
            dv.setUint16(36, 0, true);         // Internal attributes
            dv.setUint32(38, file.attributes, true); // External attributes
            dv.setUint32(42, file.offset, true);      // Rel offset of local header

            header.uint8.set(file.filenameBytes, 46);

            parts.push(header.uint8);
            offset += headerSize;
        }

        const centralDirEnd = offset;
        const centralDirSize = centralDirEnd - centralDirStart;
        const centralDirCount = centralDirectory.length;

        // End of Central Directory Record
        // 22 bytes
        const eocd = createBuffer(22);
        const dv = eocd.view;

        dv.setUint32(0, 0x06054b50, true); // Signature
        dv.setUint16(4, 0, true);          // Disk number
        dv.setUint16(6, 0, true);          // Disk num with CD
        dv.setUint16(8, centralDirCount, true); // Num entries in CD on this disk
        dv.setUint16(10, centralDirCount, true); // Total num entries in CD
        dv.setUint32(12, centralDirSize, true);  // Size of CD
        dv.setUint32(16, centralDirStart, true); // Offset of CD
        dv.setUint16(20, 0, true);         // Comment length

        parts.push(eocd.uint8);

        return new Blob(parts, { type: 'application/zip' });
    }
}

export default SimpleZip;
