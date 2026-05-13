/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./index.html', './docs.html'],
  theme: {
    extend: {
      colors: {
        brand: {
          50:  '#f0f4ff',
          100: '#dce6ff',
          200: '#b9ccff',
          300: '#849eff',
          400: '#4a6bfa',
          500: '#2f4de0',
          600: '#2236c2',
          700: '#1c2a9e',
          800: '#1c2880',
          900: '#1b2668',
        },
      },
    },
  },
  plugins: [],
}
