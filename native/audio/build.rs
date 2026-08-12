fn main() {
    pkg_config::probe_library("jack").unwrap();
}
