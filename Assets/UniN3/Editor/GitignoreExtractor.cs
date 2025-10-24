// MIT License
// 
// Copyright (c) 2025 nae3na
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace UniN3.Editor {
    /// <summary>
    /// GitignoreExtractor
    /// </summary>
    public class GitignoreExtractor : EditorWindow {
        private const string WINDOW_TITLE = "Gitignore Extractor";
        private const string GITIGNORE_FILE_NAME = ".gitignore";
        private const string OUTPUT_FOLDER_NAME = "gitignore";
        private const float VERTICAL_SPACE_AFTER_LIST = 5f;
        private const double REBUILD_INTERVAL_SEC = 0.5;

        private string m_ProjectRootPath;       // Project root (parent of Assets)
        private string m_GitignoreFilePath;     // Absolute path to .gitignore
        private string m_OutputParentPath;      // Where to create folder
        private string m_OutputFolderName;

        private Vector2 m_ScrollPos;
        private Vector2 m_ScrollPreview;
        private bool m_PreviewFoldout = false;
        private List<string> m_ExtraTopDirs = new List<string>( );          // Additional top-level dirs (besides Assets)
        private List<string> m_MatchedPathsPreview = new List<string>( );   // For UI preview

        private SortedSet<string> m_IgnoredFiles = new SortedSet<string>( StringComparer.Ordinal ); // project-relative files
        private SortedSet<string> m_IgnoredDirs = new SortedSet<string>( StringComparer.Ordinal );  // project-relative dirs (no trailing slash)

        private bool m_AutoRefreshPreview = false;

        private GitignoreMatcher m_Matcher;
        private double m_LastRebuildTime;

        // caches to reduce repeated checks
        private readonly Dictionary<string, bool> m_DirIgnoredCache = new Dictionary<string, bool>( StringComparer.Ordinal );
        private readonly HashSet<string> s_TempAncestorMetas = new HashSet<string>( StringComparer.Ordinal );

        /// <summary>
        /// Opens the Gitignore Extractor window.
        /// </summary>
        [MenuItem( "Tools/Gitignore Extractor" )]
        public static void Open( ) {
            var wnd = GetWindow<GitignoreExtractor>( WINDOW_TITLE );
            wnd.minSize = new Vector2( 720, 540 );
            wnd.Show( );
        }

        /// <summary>
        /// Unity OnEnable
        /// </summary>
        private void OnEnable( ) {
            // Initializes default paths and builds matcher if .gitignore exists.

            string projectRoot = Directory.GetParent( Application.dataPath ).FullName;
            m_ProjectRootPath = projectRoot;
            m_OutputParentPath = projectRoot;
            m_OutputFolderName = OUTPUT_FOLDER_NAME;

            string defaultGi = Path.Combine( projectRoot, GITIGNORE_FILE_NAME );
            if ( File.Exists( defaultGi ) ) {
                m_GitignoreFilePath = defaultGi;
                TryBuildMatcher( );
            }
        }

        /// <summary>
        /// Unity OnGUI
        /// </summary>
        private void OnGUI( ) {
            OnInspectorGUI( );
        }

        /// <summary>
        /// Draws the window GUI.
        /// </summary>
        private void OnInspectorGUI( ) {
            using ( var scroll = new EditorGUILayout.ScrollViewScope( m_ScrollPos ) ) {
                m_ScrollPos = scroll.scrollPosition;

                EditorGUILayout.LabelField( ".gitignore location", EditorStyles.boldLabel );
                using ( new EditorGUILayout.HorizontalScope( ) ) {
                    using ( new EditorGUI.DisabledScope( true ) ) {
                        m_GitignoreFilePath = EditorGUILayout.TextField( "gitignore", m_GitignoreFilePath ?? string.Empty );
                    }

                    if ( GUILayout.Button( "...", GUILayout.Width( 30 ) ) ) {
                        string startDir = Directory.Exists( m_ProjectRootPath ) ? m_ProjectRootPath : Directory.GetCurrentDirectory( );
                        string sel = EditorUtility.OpenFilePanel( "Select .gitignore", startDir, "" );
                        if ( !string.IsNullOrEmpty( sel ) && Path.GetFileName( sel ) == GITIGNORE_FILE_NAME ) {
                            m_GitignoreFilePath = sel;
                            m_ProjectRootPath = Directory.GetParent( sel ).FullName;
                            if ( string.IsNullOrEmpty( m_OutputParentPath ) )
                                m_OutputParentPath = m_ProjectRootPath;

                            TryBuildMatcher( );
                            if ( !m_AutoRefreshPreview ) Repaint( );
                        }
                        else if ( !string.IsNullOrEmpty( sel ) ) {
                            EditorUtility.DisplayDialog( "Selection Error", "Please select a .gitignore file.", "OK" );
                        }
                    }
                }

                EditorGUILayout.Space( );

                EditorGUILayout.LabelField( "Top-level dirs to include besides \"Assets\" (e.g. Packages)", EditorStyles.boldLabel );
                DrawExtraTopDirsList( );

                EditorGUILayout.Space( );

                EditorGUILayout.LabelField( "Preview of ignored paths (to be exported)", EditorStyles.boldLabel );
                m_PreviewFoldout = EditorGUILayout.Foldout( m_PreviewFoldout, "Ignored paths", true );
                if ( m_PreviewFoldout ) {
                    using ( new EditorGUILayout.VerticalScope( GUI.skin.box ) ) {
                        using ( var scrollPreview = new EditorGUILayout.ScrollViewScope( m_ScrollPreview, GUILayout.Height( 200 ) ) ) {
                            m_ScrollPreview = scrollPreview.scrollPosition;

                            if ( m_MatchedPathsPreview.Count == 0 ) {
                                EditorGUILayout.LabelField( "(No preview)" );
                            }
                            else {
                                foreach ( var p in m_MatchedPathsPreview ) {
                                    EditorGUILayout.LabelField( p );
                                }
                            }
                        }
                    }
                }

                GUILayout.Space( VERTICAL_SPACE_AFTER_LIST );

                EditorGUILayout.LabelField( "Output parent", EditorStyles.boldLabel );
                using ( new EditorGUILayout.HorizontalScope( ) ) {
                    using ( new EditorGUI.DisabledScope( true ) ) {
                        EditorGUILayout.TextField( "Output Parent", string.IsNullOrEmpty( m_OutputParentPath ) ? "" : m_OutputParentPath );
                    }
                    if ( GUILayout.Button( "...", GUILayout.Width( 30 ) ) ) {
                        string startDir = Directory.Exists( m_OutputParentPath ) ? m_OutputParentPath : m_ProjectRootPath;
                        string sel = EditorUtility.OpenFolderPanel( "Select output parent", startDir, "" );
                        if ( !string.IsNullOrEmpty( sel ) ) {
                            m_OutputParentPath = sel;
                        }
                    }
                }
                m_OutputFolderName = EditorGUILayout.TextField( "Output folder name", m_OutputFolderName );

                EditorGUILayout.Space( );

                using ( new EditorGUILayout.HorizontalScope( ) ) {
                    bool auto = EditorGUILayout.ToggleLeft( "Auto-refresh preview", m_AutoRefreshPreview, GUILayout.Width( 180 ) );
                    if ( auto != m_AutoRefreshPreview ) {
                        m_AutoRefreshPreview = auto;
                    }

                    if ( GUILayout.Button( "Refresh Preview", GUILayout.Width( 140 ) ) ) {
                        RebuildPreview( );
                    }
                }

                EditorGUILayout.Space( );

                using ( new EditorGUI.DisabledScope( string.IsNullOrEmpty( m_GitignoreFilePath ) || !File.Exists( m_GitignoreFilePath ) ) ) {
                    if ( GUILayout.Button( $"Export (create {m_OutputFolderName} folder and copy)", GUILayout.Height( 32 ) ) ) {
                        DoExport( );
                    }
                }

                if ( m_AutoRefreshPreview && ( EditorApplication.timeSinceStartup - m_LastRebuildTime > REBUILD_INTERVAL_SEC ) ) {
                    if ( m_Matcher != null ) {
                        RebuildPreview( );
                    }
                }
            }
        }

        /// <summary>
        /// Draws the editable list of extra top-level directories.
        /// </summary>
        private void DrawExtraTopDirsList( ) {
            using ( new EditorGUILayout.VerticalScope( GUI.skin.box ) ) {
                int removeIndex = -1;
                for ( int i = 0; i < m_ExtraTopDirs.Count; i++ ) {
                    using ( new EditorGUILayout.HorizontalScope( ) ) {
                        string before = m_ExtraTopDirs[ i ];
                        string after = EditorGUILayout.TextField( before );
                        if ( after != before ) {
                            m_ExtraTopDirs[ i ] = after;
                            if ( m_AutoRefreshPreview ) RebuildPreview( );
                        }
                        if ( GUILayout.Button( "Remove", GUILayout.Width( 80 ) ) ) removeIndex = i;
                    }
                }

                if ( removeIndex >= 0 ) {
                    m_ExtraTopDirs.RemoveAt( removeIndex );
                    if ( m_AutoRefreshPreview ) RebuildPreview( );
                }

                using ( new EditorGUILayout.HorizontalScope( ) ) {
                    if ( GUILayout.Button( "Add", GUILayout.Width( 80 ) ) ) {
                        m_ExtraTopDirs.Add( string.Empty );
                    }
                    GUILayout.FlexibleSpace( );
                    if ( GUILayout.Button( "Trim empty", GUILayout.Width( 100 ) ) ) {
                        m_ExtraTopDirs.RemoveAll( s => string.IsNullOrWhiteSpace( s ) );
                        if ( m_AutoRefreshPreview ) RebuildPreview( );
                    }
                }
            }
        }


        /// <summary>
        /// Builds the internal matcher from the current .gitignore file.
        /// </summary>
        private void TryBuildMatcher( ) {
            m_Matcher = null;
            if ( string.IsNullOrEmpty( m_GitignoreFilePath ) || !File.Exists( m_GitignoreFilePath ) ) return;

            try {
                string[ ] lines = File.ReadAllLines( m_GitignoreFilePath, new UTF8Encoding( false ) );
                var matcher = new GitignoreMatcher( lines );
                m_Matcher = matcher;
            }
            catch ( Exception e ) {
                UnityEngine.Debug.LogError( "[GitignoreExtractor] Failed to parse .gitignore: " + e );
                m_Matcher = null;
            }
        }

        /// <summary>
        /// Rebuilds the preview list of ignored paths (directories first, then files).
        /// </summary>
        private void RebuildPreview( ) {
            m_LastRebuildTime = EditorApplication.timeSinceStartup;
            m_MatchedPathsPreview.Clear( );
            m_IgnoredFiles.Clear( );
            m_IgnoredDirs.Clear( );
            m_DirIgnoredCache.Clear( );
            if ( m_Matcher == null ) return;

            // Assets via AssetDatabase
            var all = AssetDatabase.GetAllAssetPaths( );    // e.g. "Assets/..."
            foreach ( var rel in all ) {
                if ( !rel.StartsWith( "Assets/" ) ) continue;
                AddIfIgnoredFileOrDir( rel );
            }

            // Extra top-level directories via I/O
            foreach ( var d in m_ExtraTopDirs ) {
                var dir = d?.Trim( )?.Trim( '/', '\\' );
                if ( string.IsNullOrEmpty( dir ) || string.Equals( dir, "Assets", StringComparison.OrdinalIgnoreCase ) ) continue;
                string full = Path.Combine( m_ProjectRootPath, dir );
                if ( Directory.Exists( full ) ) CollectByIO( full );
            }

            // Supplement Assets with I/O for special directories (e.g., "Samples~")
            SupplementAssetsByIOForSpecialDirs( );

            // Ensure all ignored ancestors are included as directories
            EnsureAllIgnoredAncestors( );

            // Add ancestor folder .meta files (skip only when explicitly negated by '!')
            IncludeAncestorFolderMetas( );

            // Build preview (dirs first)
            foreach ( var dir in m_IgnoredDirs ) m_MatchedPathsPreview.Add( dir + "/" );
            foreach ( var file in m_IgnoredFiles ) m_MatchedPathsPreview.Add( file );
        }

        /// <summary>
        /// Adds a file or directory to the ignored lists if it matches .gitignore rules.
        /// For files, also checks and includes associated .meta files.
        /// </summary>
        /// <param name="_rel">Project-relative path</param>
        private void AddIfIgnoredFileOrDir( string _rel ) {
            if ( Path.HasExtension( _rel ) ) {
                if ( m_Matcher.IsIgnored( _rel ) ) {
                    m_IgnoredFiles.Add( _rel );
                    var meta = _rel + ".meta";
                    // Try including file meta (existence checked via absolute path)
                    if ( File.Exists( Path.Combine( m_ProjectRootPath, meta ) ) )
                        m_IgnoredFiles.Add( meta );
                }

                // Check parent directory as potential ignored candidate (for empty dir creation)
                var dir = Path.GetDirectoryName( _rel )?.Replace( '\\', '/' );
                if ( !string.IsNullOrEmpty( dir ) && IsDirectoryIgnored( dir ) ) m_IgnoredDirs.Add( dir );
                return;
            }

            var relDir = _rel.Replace( '\\', '/' ).TrimEnd( '/' );
            if ( IsDirectoryIgnored( relDir ) ) m_IgnoredDirs.Add( relDir );
        }

        /// <summary>
        /// Performs direct I/O scanning for additional top-level directories (non-Assets).
        /// Collects ignored files, folders, and their .meta files recursively.
        /// </summary>
        /// <param name="_full">Absolute directory path to scan</param>
        private void CollectByIO( string _full ) {
            // Files
            try {
                foreach ( var f in Directory.GetFiles( _full, "*", SearchOption.AllDirectories ) ) {
                    var rel = MakeProjectRelative( f );
                    if ( m_Matcher.IsIgnored( rel ) ) {
                        m_IgnoredFiles.Add( rel );
                        var meta = f + ".meta";
                        if ( File.Exists( meta ) ) m_IgnoredFiles.Add( MakeProjectRelative( meta ) );
                    }
                }
            }
            catch { }

            // Directories
            try {
                foreach ( var d in Directory.GetDirectories( _full, "*", SearchOption.AllDirectories ) ) {
                    var relDir = MakeProjectRelative( d ).Replace( '\\', '/' ).TrimEnd( '/' );
                    if ( IsDirectoryIgnored( relDir ) ) m_IgnoredDirs.Add( relDir );

                    var meta = d + ".meta";
                    if ( File.Exists( meta ) ) {
                        var relMeta = MakeProjectRelative( meta );
                        if ( m_IgnoredDirs.Contains( relDir ) || m_Matcher.IsIgnored( relMeta ) )
                            m_IgnoredFiles.Add( relMeta );
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Supplement scan under Assets for directories not returned by AssetDatabase
        /// (e.g., "Samples~", names containing '~', or starting with '.').
        /// Adds such dirs to ignored set when they match ignore rules and includes their folder .meta.
        /// </summary>
        private void SupplementAssetsByIOForSpecialDirs( ) {
            string assetsAbs = Path.Combine( m_ProjectRootPath, "Assets" );
            if ( !Directory.Exists( assetsAbs ) ) return;

            var known = new HashSet<string>( AssetDatabase.GetAllAssetPaths( ), StringComparer.Ordinal );

            var stack = new Stack<string>( );
            stack.Push( assetsAbs );

            while ( stack.Count > 0 ) {
                var dirAbs = stack.Pop( );
                string name = Path.GetFileName( dirAbs );

                try {
                    foreach ( var child in Directory.GetDirectories( dirAbs ) )
                        stack.Push( child );
                }
                catch { }

                bool looksSpecial =
                    name == "Samples~" ||
                    name.StartsWith( "." ) ||
                    name.Contains( "~" );

                if ( !looksSpecial ) continue;

                string relDir = MakeProjectRelative( dirAbs );
                if ( string.IsNullOrEmpty( relDir ) ) continue;
                relDir = relDir.Replace( '\\', '/' ).TrimEnd( '/' );

                if ( !known.Contains( relDir ) && IsDirectoryIgnored( relDir ) ) {
                    m_IgnoredDirs.Add( relDir );

                    string metaAbs = dirAbs + ".meta";
                    if ( File.Exists( metaAbs ) ) {
                        string relMeta = relDir + ".meta";
                        var kind = ( m_Matcher != null ) ? m_Matcher.GetLastMatchKind( relMeta ) : GitignoreMatchKind.None;
                        if ( kind != GitignoreMatchKind.Negate )
                            m_IgnoredFiles.Add( relMeta );
                    }
                }
            }
        }

        /// <summary>
        /// Recursively collects ignored files and directories under the given full path directory.
        /// If a path is resurrected by '!', it will not be included.
        /// </summary>
        private void CollectIgnoredEntriesRecursive( string _dirFull ) {
            // (Kept for backward compatibility if you still call this somewhere else.)
            string relDir = MakeProjectRelative( _dirFull );
            if ( IsDirectoryIgnored( relDir ) ) m_IgnoredDirs.Add( relDir );

            var files = SafeGetFiles( _dirFull );
            foreach ( var f in files ) {
                string rel = MakeProjectRelative( f );
                if ( m_Matcher.IsIgnored( rel ) ) {
                    m_IgnoredFiles.Add( rel );

                    string meta = f + ".meta";
                    if ( File.Exists( meta ) ) {
                        m_IgnoredFiles.Add( MakeProjectRelative( meta ) );
                    }
                }
            }

            var dirs = SafeGetDirectories( _dirFull );
            foreach ( var d in dirs ) {
                CollectIgnoredEntriesRecursive( d );

                string folderMetaPath = d + ".meta";
                if ( File.Exists( folderMetaPath ) ) {
                    string relMeta = MakeProjectRelative( folderMetaPath );
                    string relParent = MakeProjectRelative( d );

                    if ( m_IgnoredDirs.Contains( relParent ) || m_Matcher.IsIgnored( relMeta ) ) {
                        m_IgnoredFiles.Add( relMeta );
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the directory itself (or its children via trailing slash rule) are ignored.
        /// This also detects rules like "Temp/" which imply "Temp/**".
        /// </summary>
        private bool IsDirectoryIgnored( string _relDir ) {
            string rel = _relDir.Replace( '\\', '/' ).TrimEnd( '/' );
            if ( string.IsNullOrEmpty( rel ) ) return false;

            if ( m_DirIgnoredCache.TryGetValue( rel, out var v ) ) return v;

            bool ignored =
                m_Matcher.IsIgnored( rel ) ||               // direct directory match (e.g., "**/Temp")
                m_Matcher.IsIgnored( rel + "/__probe__" );  // simulate "dir/**" (e.g., "Temp/")
            m_DirIgnoredCache[ rel ] = ignored;
            return ignored;
        }

        /// <summary>
        /// Ensures that all ignored ancestor directories (up to project root) are included
        /// so that empty parent directories are created as well.
        /// </summary>
        private void EnsureAllIgnoredAncestors( ) {
            var toAdd = new HashSet<string>( StringComparer.Ordinal );

            void AddAncestors( string relPath ) {
                string p = relPath.Replace( '\\', '/' ).Trim( '/' );
                if ( string.IsNullOrEmpty( p ) ) return;

                int slash = p.LastIndexOf( '/' );
                while ( slash > 0 ) {
                    string parent = p.Substring( 0, slash );
                    if ( IsDirectoryIgnored( parent ) ) {
                        toAdd.Add( parent );
                    }
                    slash = parent.LastIndexOf( '/' );
                }
            }

            foreach ( var f in m_IgnoredFiles ) AddAncestors( f );
            foreach ( var d in m_IgnoredDirs ) AddAncestors( d );

            foreach ( var a in toAdd ) m_IgnoredDirs.Add( a );
        }

        /// <summary>
        /// Adds .meta files for all ancestor directories of the already collected files and directories.
        /// Skips only when the ancestor .meta is explicitly resurrected by a '!' rule.
        /// </summary>
        private void IncludeAncestorFolderMetas( ) {
            s_TempAncestorMetas.Clear( );

            void AddAncestorsOf( string relPath ) {
                var p = relPath.Replace( '\\', '/' ).TrimEnd( '/' );
                int slash = p.LastIndexOf( '/' );
                while ( slash > 0 ) {
                    string parentDir = p.Substring( 0, slash );     // e.g. Assets/Application
                    string metaRel = parentDir + ".meta";           // e.g. Assets/Application.meta

                    string metaAbs = Path.Combine( m_ProjectRootPath, metaRel );
                    if ( File.Exists( metaAbs ) ) {
                        var kind = ( m_Matcher != null ) ? m_Matcher.GetLastMatchKind( metaRel ) : GitignoreMatchKind.None;
                        if ( kind != GitignoreMatchKind.Negate )
                            s_TempAncestorMetas.Add( metaRel );
                    }
                    slash = parentDir.LastIndexOf( '/' );
                }
            }

            foreach ( var f in m_IgnoredFiles ) AddAncestorsOf( f );
            foreach ( var d in m_IgnoredDirs ) AddAncestorsOf( d );

            foreach ( var meta in s_TempAncestorMetas )
                m_IgnoredFiles.Add( meta );
        }


        /// <summary>
        /// Exports the previewed files/directories into a unique folder under the selected output parent.
        /// </summary>
        private void DoExport( ) {
            if ( m_Matcher == null ) {
                EditorUtility.DisplayDialog( "Error", ".gitignore is not set.", "OK" );
                return;
            }
            if ( string.IsNullOrEmpty( m_OutputParentPath ) || !Directory.Exists( m_OutputParentPath ) ) {
                EditorUtility.DisplayDialog( "Error", "Output parent folder is invalid.", "OK" );
                return;
            }

            string outDir = Path.Combine( m_OutputParentPath, OUTPUT_FOLDER_NAME );
            string unique = MakeUniqueFolder( outDir );

            try {
                // Create directories first (including empty, ignored parents) and try copying folder .meta
                foreach ( var relDir in m_IgnoredDirs ) {
                    string srcDir = Path.Combine( m_ProjectRootPath, relDir );
                    string dstDir = Path.Combine( unique, relDir );
                    Directory.CreateDirectory( dstDir );

                    string srcMeta = srcDir + ".meta";
                    if ( File.Exists( srcMeta ) ) {
                        string dstMeta = dstDir + ".meta";
                        Directory.CreateDirectory( Path.GetDirectoryName( dstMeta ) );
                        File.Copy( srcMeta, dstMeta, true );

                        // ensure folder .meta is also in files set (so counts reflect it)
                        if ( !m_IgnoredFiles.Contains( relDir + ".meta" ) )
                            m_IgnoredFiles.Add( relDir + ".meta" );
                    }
                }

                // Copy files (negation already respected by IsIgnored/GetLastMatchKind in collectors)
                int copiedCount = 0;
                foreach ( var rel in m_IgnoredFiles ) {
                    string src = Path.Combine( m_ProjectRootPath, rel );
                    string dst = Path.Combine( unique, rel );
                    string dstDir = Path.GetDirectoryName( dst );
                    if ( !string.IsNullOrEmpty( dstDir ) ) Directory.CreateDirectory( dstDir );

                    if ( File.Exists( src ) ) {
                        File.Copy( src, dst, true );
                        copiedCount++;
                    }
                }

                EditorUtility.RevealInFinder( unique );
                EditorUtility.DisplayDialog( "Done",
                    $"Output:\n{unique}\n\n" +
                    $"Created directories: {m_IgnoredDirs.Count}\n" +
                    $"Copied files: {copiedCount}", "OK" );
            }
            catch ( Exception e ) {
                UnityEngine.Debug.LogError( "[GitignoreExtractor] Export failed: " + e );
                EditorUtility.DisplayDialog( "Error", "Export failed. See Console for details.", "OK" );
            }
        }

        /// <summary>
        /// Creates a unique folder by appending (1), (2), ...
        /// </summary>
        private static string MakeUniqueFolder( string _basePath ) {
            if ( !Directory.Exists( _basePath ) ) return _basePath;
            int idx = 1;
            while ( true ) {
                string candidate = _basePath + $" ({idx})";
                if ( !Directory.Exists( candidate ) ) return candidate;
                idx++;
            }
        }


        /// <summary>
        /// Gets files in a directory, returns empty array if inaccessible.
        /// </summary>
        private static string[ ] SafeGetFiles( string _dir ) {
            try { return Directory.GetFiles( _dir ); }
            catch { return Array.Empty<string>( ); }
        }

        /// <summary>
        /// Gets subdirectories in a directory, returns empty array if inaccessible.
        /// </summary>
        private static string[ ] SafeGetDirectories( string _dir ) {
            try { return Directory.GetDirectories( _dir ); }
            catch { return Array.Empty<string>( ); }
        }

        /// <summary>
        /// Makes a project-relative path (e.g., "Assets/...").
        /// </summary>
        private string MakeProjectRelative( string _fullPath ) {
            string p = _fullPath.Replace( '\\', '/' );
            string root = m_ProjectRootPath.Replace( '\\', '/' ).TrimEnd( '/' );
            if ( p.StartsWith( root + "/", StringComparison.Ordinal ) ) {
                return p.Substring( root.Length + 1 );
            }
            return p;
        }
    }


    /// <summary>
    /// .gitignore Matcher
    /// - Ignores comments/empty lines
    /// - Supports '!' (negation), with "last match wins" semantics
    /// - Trailing '/' treated as directory -> appends '/**'
    /// - Patterns without leading '/' are considered to match at any depth ('**/' prefix)
    /// - Supports '**', '*', '?'
    /// - Evaluates against project-relative paths using forward slashes
    /// - Exposes GetLastMatchKind() to distinguish Ignore/Negate/None (for ancestor .meta logic)
    /// </summary>
    internal enum GitignoreMatchKind {
        None,       // no rule matched
        Ignore,     // last matched rule is a positive ignore
        Negate      // last matched rule is a '!' negation
    }

    internal sealed class GitignoreMatcher {
        private struct Rule {
            public Regex regex;
            public bool negation;
        }

        private readonly List<Rule> m_Rules = new List<Rule>( );
        private readonly Dictionary<string, bool> m_Cache = new Dictionary<string, bool>( StringComparer.Ordinal );

        /// <summary>
        /// Builds a matcher from .gitignore lines.
        /// </summary>
        public GitignoreMatcher( IEnumerable<string> _lines ) {
            foreach ( var raw in _lines ) {
                string line = raw.Trim( );

                // Comments / empty lines
                if ( string.IsNullOrEmpty( line ) || line.StartsWith( "#" ) ) continue;

                bool neg = false;
                if ( line.StartsWith( "!" ) ) {
                    neg = true;
                    line = line.Substring( 1 );
                }

                line = line.Replace( '\\', '/' ).Trim( );

                // A trailing '/' means directory → include descendants
                if ( line.EndsWith( "/" ) ) {
                    line = line + "**";
                }

                // If not root-anchored, match at any depth
                bool rootAnchored = line.StartsWith( "/" );
                if ( !rootAnchored ) {
                    if ( !line.StartsWith( "**/" ) )
                        line = "**/" + line;
                }
                else {
                    line = line.TrimStart( '/' );
                }

                string pattern = GlobToRegex( line );

                try {
                    var rx = new Regex( "^" + pattern + "$", RegexOptions.Compiled );
                    m_Rules.Add( new Rule { regex = rx, negation = neg } );
                }
                catch ( Exception e ) {
                    UnityEngine.Debug.LogWarning( "[GitignoreExtractor] Failed to compile regex for rule: " + raw + " / " + e.Message );
                }
            }
        }

        /// <summary>
        /// Returns whether the path is ignored (last-match-wins). Uses a simple cache.
        /// Evaluates rules from the end for early-exit on the first match.
        /// </summary>
        public bool IsIgnored( string _projectRelativePath ) {
            string rel = _projectRelativePath.Replace( '\\', '/' ).TrimStart( '/' );

            if ( m_Cache.TryGetValue( rel, out var hit ) ) return hit;

            for ( int i = m_Rules.Count - 1; i >= 0; i-- ) {
                var r = m_Rules[ i ];
                if ( r.regex.IsMatch( rel ) ) {
                    bool ignored = !r.negation;
                    m_Cache[ rel ] = ignored;
                    return ignored;
                }
            }
            m_Cache[ rel ] = false;
            return false;
        }

        /// <summary>
        /// Returns the kind of the last matching rule for the given path.
        /// None if no rule matches; Ignore if last is positive; Negate if last is '!' rule.
        /// </summary>
        public GitignoreMatchKind GetLastMatchKind( string _projectRelativePath ) {
            string rel = _projectRelativePath.Replace( '\\', '/' ).TrimStart( '/' );

            for ( int i = m_Rules.Count - 1; i >= 0; i-- ) {
                var r = m_Rules[ i ];
                if ( r.regex.IsMatch( rel ) )
                    return r.negation ? GitignoreMatchKind.Negate : GitignoreMatchKind.Ignore;
            }
            return GitignoreMatchKind.None;
        }

        /// <summary>
        /// Converts a glob pattern to a regex string.
        /// </summary>
        private static string GlobToRegex( string _glob ) {
            var sb = new StringBuilder( );

            for ( int i = 0; i < _glob.Length; i++ ) {
                char c = _glob[ i ];

                if ( c == '*' ) {
                    bool isDouble = ( i + 1 < _glob.Length && _glob[ i + 1 ] == '*' );
                    if ( isDouble ) {
                        if ( i + 2 < _glob.Length && _glob[ i + 2 ] == '/' ) {
                            sb.Append( ".*" );
                            i += 2; // consume "**/"
                        }
                        else {
                            sb.Append( ".*" );
                            i += 1; // consume second '*'
                        }
                    }
                    else {
                        sb.Append( "[^/]*" );
                    }
                }
                else if ( c == '?' ) {
                    sb.Append( "[^/]" );
                }
                else {
                    if ( "+()^$.{}[]|\\".IndexOf( c ) >= 0 )
                        sb.Append( '\\' );
                    sb.Append( c );
                }
            }

            return sb.ToString( );
        }
    }
}
#endif
