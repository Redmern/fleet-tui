-- The two plugins fleet drives (:Neotree and :ClaudeCode) and their dependencies. Both load
-- at startup, because fleet calls their commands from 'nvim -c' right after init.
return {
  { 'nvim-lua/plenary.nvim', lazy = true },
  { 'MunifTanjim/nui.nvim', lazy = true },
  { 'nvim-tree/nvim-web-devicons', lazy = true },
  {
    'nvim-neo-tree/neo-tree.nvim',
    branch = 'v3.x',
    lazy = false,
    dependencies = { 'nvim-lua/plenary.nvim', 'MunifTanjim/nui.nvim', 'nvim-tree/nvim-web-devicons' },
    opts = {
      filesystem = {
        follow_current_file = { enabled = true },
        use_libuv_file_watcher = true,
        filtered_items = { visible = true, hide_dotfiles = false, hide_gitignored = false },
      },
      window = {
        width = 45,
        mappings = { ['<space>'] = 'none', ['l'] = 'open', ['h'] = 'close_node' },
      },
    },
  },
  {
    'coder/claudecode.nvim',
    lazy = false,
    config = function()
      require('fleet.claudecode').setup()
    end,
  },
}
