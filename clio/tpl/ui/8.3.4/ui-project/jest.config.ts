import type { Config } from 'jest';

export default {
  preset: 'jest-preset-angular',
  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
} satisfies Config;
