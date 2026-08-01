---
name: C++ Testing
description: Follow these standards when developing C++ tests.
globs: ["**/test/**/*.cpp", "**/tests/**/*.cpp", "**/*_test.cpp", "**/*_tests.cpp"]
---

# Required Standards

Read these standards first before applying this standard:

- **`testing-principles.md`** - Test lifecycles and universal testing principles
- **`cpp-language.md`** - C++ language development standards

# File Organization

Tests are split by lifecycle, not by source structure:

```text
test/
└── {system_name}_tests/
    ├── contract/
    │   └── {system_name}_contract_tests.cpp   # durable - one per clause
    └── ...                                    # interior tests - disposable
```

Contract tests live under `contract/` so their durability is visible. Interior
tests sit alongside and may be organized however is convenient — they are deleted
with the code they cover.

# Package Reference

Use `GTest` and `GMock` from the CMake `GTest` package. Link test targets with
`GTest::gtest_main` and `GTest::gmock`.

# Test Style

Contract test names are written verbatim into the clause they verify, so they must
be stable and greppable. Use snake_case split across the gtest suite and test name:

- **Contract tests**: `TEST({system_name}_contract_test, {clause_behavior})`
- **Interior tests**: `TEST({subject}_test, {functionality}_{scenario}_{expected_behavior})`
- Use `TEST_F` with a fixture class when shared setup is needed

```cpp
/// @brief Validates that an invalid email format throws std::invalid_argument.
TEST(user_validator_test, validate_email_invalid_format_throws)
{
    // Arrange: create a validator with default configuration
    user_validator validator;

    // Act / Assert: email with no domain throws
    EXPECT_THROW(validator.validate_email("not-an-email"), std::invalid_argument);
}
```

# gtest/gmock Specifics

These are non-obvious behaviors that differ from common assumptions:

- **`EXPECT_*` vs `ASSERT_*`**: `ASSERT_*` aborts the test immediately; prefer
  `EXPECT_*` for independent checks to surface all failures in one run
- **`EXPECT_CALL` placement**: all mock expectations must be set up in Arrange,
  before the Act step - expectations placed after the call under test are never triggered
- **`NiceMock` vs `StrictMock`**: bare mocks warn on unexpected calls; `NiceMock`
  silences them; `StrictMock` makes them failures - choose deliberately

# Quality Checks

- [ ] All tests follow AAA pattern with descriptive section comments
- [ ] Contract tests live under `contract/` and use only the public API from `include/`
- [ ] Contract test names match the clause that names them, exactly
- [ ] Each test verifies single, specific behavior (no shared state between tests)
- [ ] Both success and failure scenarios covered including edge cases
- [ ] External dependencies mocked with GMock in interior tests
- [ ] Test results generated in JUnit XML format (`--gtest_output=xml`)
