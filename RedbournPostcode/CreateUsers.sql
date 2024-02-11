--- This is the create script for the users table to be used in the SQLite database users.sqlite
create table main.users
(
    email     TEXT collate NOCASE not null,
    remaining integer,
    disabled  integer,
    constraint users_pk
        primary key (email, disabled)
);

create unique index users_email_uindex
    on main.users (email);
