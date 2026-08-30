// This is the exact legacy TGOS address-query contract from D:/work/new/src/utils.js.
// The service is prescribed externally: URL, method, content type and form field names are
// deliberately constants rather than deployment options.
export const LEGACY_TGOS_ADDRESS_URL = 'https://addr.tgos.tw/addrws/v30/QueryAddr.asmx/QueryAddr';

export const LEGACY_TGOS_ADDRESS_APP_ID = 'dXNHhv5on1mesfZ3eEICle33e/hStnEriDI2uTe4yFuJ7jlV/J1pXQ==';
export const LEGACY_TGOS_ADDRESS_API_KEY = 'cGEErDNy5yNr14zbsE/4GSfiGP5i3PuZAcQcID3Okxazx7mnV3vAIlFD5qC8whhRtGzG7SEMxQLHBp4zNyVPecDQKcV0OX3WQUwUmD+6kpcD7exBobmKnjkUj/oRy0SZQkvvnLl8h0zb0y8qzc6yUvi1VjzhL55gRKDIm5XysLnsnC1E6tVu6hM76Nf7DTC20Chz7bz9dj48AQVBF+RRxdGSLCNWd/lHZf7GGXfFPfdBPFx9Rh4Csa2cTaT/WfpiPryBTqkzc6TRrWDvYTbTVrNmxTHK3wu7/jCKD9Z5JFj4snpwGwZlQnhrw0YqumoVc32hOn4b8VE27TaRlDdpYVxikX5fBZFAVoiE0Ww4wmA1TbNpc3AQgDhCsfZiTI3kADI8SLsW0wuW1SoSVPubEyChNRhKHZu6CJLkr/bwcAotj9JaAPExPCD/rdstmI6J8sBwDqfPqkRjvTYv1IqkbQ==';

export function createLegacyTgosAddressBody(address) {
    return new URLSearchParams({
        oAPPId: LEGACY_TGOS_ADDRESS_APP_ID,
        oAPIKey: LEGACY_TGOS_ADDRESS_API_KEY,
        oAddress: String(address || ''),
        oSRS: 'EPSG:4326',
        oFuzzyType: '1',
        oResultDataType: 'JSON',
        oFuzzyBuffer: '0',
        oIsOnlyFullMatch: 'false',
        oIsLockCounty: 'false',
        oIsLockTown: 'false',
        oIsLockVillage: 'false',
        oIsLockRoadSection: 'false',
        oIsLockLane: 'false',
        oIsLockAlley: 'false',
        oIsLockArea: 'false',
        oIsSameNumber_SubNumber: 'false',
        oCanIgnoreVillage: 'true',
        oCanIgnoreNeighborhood: 'true',
        oReturnMaxCount: '1',
    });
}

function parseLegacyXmlPayload(text) {
    if (text.includes('認證授權失敗')) throw new Error('TGOS 地址服務認證授權失敗');
    const documentRef = new DOMParser().parseFromString(text, 'application/xml');
    if (documentRef.querySelector('parsererror')) throw new Error('TGOS 地址服務回應不是有效 XML');
    const candidates = Array.from(documentRef.querySelectorAll('*'))
        .map(element => element.children.length === 0 ? element.textContent?.trim() : '')
        .filter(Boolean);
    for (const candidate of candidates) {
        try {
            const parsed = JSON.parse(candidate);
            if (parsed && typeof parsed === 'object') return parsed;
        } catch { /* keep looking for the JSON-bearing SOAP element */ }
    }
    throw new Error('TGOS 地址服務回應缺少 JSON 結果');
}

export async function queryLegacyTgosAddress(address, { fetchImpl = fetch } = {}) {
    const response = await fetchImpl(LEGACY_TGOS_ADDRESS_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
        body: createLegacyTgosAddressBody(address),
        credentials: 'omit',
    });
    if (!response.ok) throw new Error(`TGOS 地址服務失敗 (HTTP ${response.status})`);
    const payload = parseLegacyXmlPayload(await response.text());
    const match = payload?.AddressList?.[0];
    if (!match) throw new Error('TGOS 地址服務查無結果');
    return {
        RealAddress: match.FULL_ADDR,
        lon: match.X,
        lat: match.Y,
    };
}
