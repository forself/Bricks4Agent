import { describe, expect, it } from "vitest";
import catalog from "../../templates/catalog.json";

describe("template catalog", () => {
  it("contains the initial interaction-structure families", () => {
    expect(catalog.families.map((entry) => entry.template_id)).toEqual([
      "content_showcase",
      "form_collection",
      "member_portal",
      "list_search",
      "crud_admin",
      "dashboard",
      "multi_step_flow",
      "transaction_flow",
    ]);
  });
});
