import js from '@eslint/js'
import prettier from 'eslint-config-prettier'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import globals from 'globals'
import tseslint from 'typescript-eslint'

export default tseslint.config(
  { ignores: ['dist', 'coverage', 'src/shared/api/schema.ts'] },
  {
    extends: [
      js.configs.recommended,
      ...tseslint.configs.recommendedTypeChecked,
      reactRefresh.configs.vite,
      prettier,
    ],
    files: ['**/*.{ts,tsx}'],
    // Preset 'recommended-latest' của eslint-plugin-react-hooks vẫn khai `plugins`
    // dạng mảng — ESLint 10 flat config từ chối. Đăng ký plugin và rule thủ công
    // cho tới khi upstream sửa.
    plugins: { 'react-hooks': reactHooks },
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      ...reactHooks.configs['recommended-latest'].rules,
      // CONTEXT.md §7: không `any`. Dùng `unknown` rồi thu hẹp.
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/consistent-type-imports': [
        'error',
        { prefer: 'type-imports', fixStyle: 'inline-type-imports' },
      ],
    },
  },
  {
    // Config chạy trên Node, không phải trình duyệt.
    files: ['*.config.{ts,js}'],
    languageOptions: { globals: globals.node },
  },
  {
    // Mã shadcn/ui chép từ upstream (§3). Nó export cả component lẫn biến
    // variant trong một file, việc mà rule fast-refresh cấm. Sửa mã đó sẽ bị đè
    // mất khi chép lại bản mới từ upstream, nên tắt rule ở đây thay vì sửa file.
    files: ['src/components/ui/**/*.tsx'],
    rules: { 'react-refresh/only-export-components': 'off' },
  },
)
