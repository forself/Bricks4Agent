import { validateDraft } from './model.mjs';
import { defaultCandidateWiring, generateBindings, generateCandidates, validatePagePlan } from './page-planner.mjs';

export const WORKSPACE_KEY = 'b4a-presale-workspace-v1';

export function validateProject(value) {
    if (!value || typeof value !== 'object' || Array.isArray(value) ||
        Object.keys(value).sort().join('|') !== 'draft|plan|schema_version' || value.schema_version !== 1)
        throw new TypeError('工作檔版本或格式無效。');
    validateDraft(value.draft, { requireConfirmation: false });
    const confirmedDraft = { ...value.draft, confirmed: true };
    validatePagePlan(value.plan, generateCandidates(confirmedDraft), confirmedDraft);
    return value;
}

export function projectSnapshot(draft, plan) {
    return validateProject({ schema_version: 1, draft: structuredClone(draft), plan: structuredClone(plan) });
}

export function recoverableSnapshot(draft, plan) {
    validateDraft(draft, { requireConfirmation: false });
    const confirmedDraft = { ...draft, confirmed: true };
    const candidates = generateCandidates(confirmedDraft);
    const names = new Set(candidates.map(item => item.registry_name));
    const sources = new Map(generateBindings(confirmedDraft).map(item => [item.ref, item]));
    const items = [];
    for (const source of plan.items) {
        if (source.kind === 'candidate' && !names.has(source.ref)) continue;
        const item = structuredClone(source);
        if (item.wiring) { items.push(item); continue; }
        try { validatePagePlan({ schema_version: 1, items: [item] }, candidates, confirmedDraft); }
        catch {
            delete item.binding;
            try { validatePagePlan({ schema_version: 1, items: [item] }, candidates, confirmedDraft); }
            catch { continue; }
        }
        items.push(item);
    }
    try { validatePagePlan({ schema_version: 1, items }, candidates, confirmedDraft); }
    catch {
        for (const item of items) if (item.wiring)
            item.wiring = defaultCandidateWiring(candidates.find(candidate => candidate.registry_name === item.ref),
                sources.get(item.ref), item.instance_id);
    }
    const recovered = { schema_version: 1, items };
    if (Array.isArray(plan.flows)) {
        const retained = [];
        for (const flow of [...plan.flows].sort((a, b) =>
            Number(b.operation === 'query') - Number(a.operation === 'query'))) {
            try { validatePagePlan({ ...recovered, flows: [...retained, flow] },
                candidates, confirmedDraft); retained.push(flow); }
            catch { /* A changed interview invalidated this flow. */ }
        }
        recovered.flows = plan.flows.filter(flow => retained.includes(flow));
    }
    return projectSnapshot(draft, recovered);
}
