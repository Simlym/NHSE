import { ref } from 'vue';
import en from './locales/en.json';

export const languages = [
  ['en', 'English'],
  ['jp', '日本語'],
  ['de', 'Deutsch'],
  ['es', 'Español'],
  ['fr', 'Français'],
  ['it', 'Italiano'],
  ['ko', '한국어'],
  ['zhs', '简体中文'],
  ['zht', '繁體中文'],
] as const;
const files = import.meta.glob<Record<string, string>>('./locales/*.json', {
  eager: true,
  import: 'default',
});
export const language = ref('en');
export function setLanguage(code: string) {
  language.value = languages.some(([id]) => id === code) ? code : 'en';
  document.documentElement.lang =
    ({ jp: 'ja', zhs: 'zh-CN', zht: 'zh-TW' } as Record<string, string>)[language.value] ?? language.value;
}
export function t(key: keyof typeof en): string {
  return files[`./locales/${language.value}.json`]?.[key] ?? en[key];
}
export function hasText(key: string): key is keyof typeof en {
  return Object.hasOwn(en, key);
}
