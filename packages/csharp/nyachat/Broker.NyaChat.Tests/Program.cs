// NyaChat 核心測試執行器:依序執行各測試類別的 Run(),任何失敗即以非零結束碼退出。
int passed = 0, failed = 0;

Console.WriteLine("=== NyaConcurrencyTests ===");
{ var (p, f) = Broker.Tests.NyaConcurrencyTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaGuestGateTests ===");
{ var (p, f) = Broker.Tests.NyaGuestGateTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaLlmAdminTests ===");
{ var (p, f) = Broker.Tests.NyaLlmAdminTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaLlmProfileStoreTests ===");
{ var (p, f) = Broker.Tests.NyaLlmProfileStoreTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaLlmTests ===");
{ var (p, f) = Broker.Tests.NyaLlmTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaMemoryQualityTests ===");
{ var (p, f) = Broker.Tests.NyaMemoryQualityTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaPromptTests ===");
{ var (p, f) = Broker.Tests.NyaPromptTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaProviderMappingTests ===");
{ var (p, f) = Broker.Tests.NyaProviderMappingTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaTechDebtTests ===");
{ var (p, f) = Broker.Tests.NyaTechDebtTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaTokenBudgetTests ===");
{ var (p, f) = Broker.Tests.NyaTokenBudgetTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaToolExecutionTests ===");
{ var (p, f) = Broker.Tests.NyaToolExecutionTests.Run(); passed += p; failed += f; }
Console.WriteLine("=== NyaTopicTests ===");
{ var (p, f) = Broker.Tests.NyaTopicTests.Run(); passed += p; failed += f; }

Console.WriteLine();
Console.WriteLine($"=== NyaChat core tests: {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;
